using Kepler.Core.Builder;
using Kepler.Core.Policy;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kepler.Core;

public static class KeplerPolicyHelper
{
    /*───────────────────────────────────────────────*/
    /*  ALLOWED FIELDS (FLATTENED VIEW)              */
    /*───────────────────────────────────────────────*/

    private static List<string> GetAllowedFields(Type entityType, string policyName, string role = "Default")
    {
        var allowed = new List<string>();

        var globalExcludes = KeplerGlobalExcludeHelper
            .GetGloballyExcludedPropertiesIncludingEFConfig(entityType);

        var policies = KeplerRegistry.GetPolicy(entityType.Name, policyName);

        if (policies.TryGetValue(role, out var scalar))
            allowed.AddRange(scalar);

        var nested = GetNestedFieldPolicies(entityType, policyName, role);

        foreach (var (nav, policy) in nested)
        {
            CollectNestedFields(nav, policy, allowed, new HashSet<string>());
        }

        allowed.RemoveAll(f =>
        {
            if (!f.Contains('.'))
                return globalExcludes.Contains(f);

            var prop = f.Split('.').Last();
            return globalExcludes.Contains(prop);
        });

        return allowed;
    }

    private static void CollectNestedFields(
        string path,
        NestedFieldPolicy policy,
        List<string> allowed,
        HashSet<string> visited)
    {
        if (!visited.Add(path))
            return;

        foreach (var field in policy.AllowedFields)
        {
            allowed.Add($"{path}.{field}");
        }

        foreach (var (childNav, childPolicy) in policy.Children)
        {
            CollectNestedFields($"{path}.{childNav}", childPolicy, allowed, visited);
        }
    }

    /*───────────────────────────────────────────────*/
    /*  EXCLUDED FIELDS                              */
    /*───────────────────────────────────────────────*/

    private static List<string> GetExcludedFields(Type entityType, string policyName, string role = "Default")
    {
        var exclusions = KeplerRegistry.GetExclusions(entityType.Name, policyName);

        return exclusions.TryGetValue(role, out var fields)
            ? new List<string>(fields)
            : new List<string>();
    }

    /*───────────────────────────────────────────────*/
    /*  ORDER BY                                    */
    /*───────────────────────────────────────────────*/

    private static List<string> GetAllowedOrderByFields(Type entityType, string policyName, string role = "Default")
    {
        var orderBy = KeplerRegistry.GetAllowedOrderByFields(entityType.Name, policyName);

        return orderBy.TryGetValue(role, out var fields)
            ? new List<string>(fields)
            : new List<string>();
    }

    /*───────────────────────────────────────────────*/
    /*  FILTERS                                     */
    /*───────────────────────────────────────────────*/

    private static Dictionary<string, FilterPolicy> GetAllowedFilters(Type entityType, string policyName, string role = "Default")
    {
        var filters = KeplerRegistry.GetAllowedFilters(entityType.Name, policyName);

        return filters.TryGetValue(role, out var fieldFilters)
            ? new Dictionary<string, FilterPolicy>(fieldFilters)
            : new Dictionary<string, FilterPolicy>();
    }

    /*───────────────────────────────────────────────*/
    /*  NESTED POLICIES                             */
    /*───────────────────────────────────────────────*/

    private static Dictionary<string, NestedFieldPolicy> GetNestedFieldPolicies(Type entityType, string policyName, string role = "Default")
    {
        var nested = KeplerRegistry.GetNestedPolicies(entityType.Name, policyName);

        return nested.TryGetValue(role, out var policies)
            ? new Dictionary<string, NestedFieldPolicy>(policies)
            : new Dictionary<string, NestedFieldPolicy>();
    }

    /*───────────────────────────────────────────────*/
    /*  DEBUG INFO                                  */
    /*───────────────────────────────────────────────*/

    public static KeplerPolicyDebugInfo GetPolicyConfiguration(Type entityType, string policyName, string role = "Default") =>
        new KeplerPolicyDebugInfo
        {
            EntityType = entityType.Name,
            PolicyName = policyName,
            Role = role,
            AllowedFields = GetAllowedFields(entityType, policyName, role),
            ExcludedFields = GetExcludedFields(entityType, policyName, role),
            AllowedOrderByFields = GetAllowedOrderByFields(entityType, policyName, role),
            AllowedFilters = GetAllowedFilters(entityType, policyName, role),
            NestedFieldPolicies = GetNestedFieldPolicies(entityType, policyName, role),
            GlobalExclusions = KeplerGlobalExcludeHelper.GetGloballyExcludedPropertiesIncludingEFConfig(entityType)
        };

    public static KeplerPolicyDebugInfo GetPolicyConfiguration<T>(string policyName, string role = "Default") where T : class =>
        GetPolicyConfiguration(typeof(T), policyName, role);

    /*───────────────────────────────────────────────*/
    /*  PRINT                                       */
    /*───────────────────────────────────────────────*/

    public static void PrintPolicyConfiguration(Type entityType, string policyName, string role = "Default")
    {
        var cfg = GetPolicyConfiguration(entityType, policyName, role);

        Console.WriteLine($"==== {cfg.EntityType} :: {cfg.PolicyName} ({cfg.Role}) ====\n");

        var scalar = cfg.AllowedFields.Where(f => !f.Contains('.')).ToList();

        Console.WriteLine("Scalar:");
        foreach (var f in scalar)
            Console.WriteLine($" - {f}");

        Console.WriteLine("\nNested:");

        foreach (var (nav, policy) in cfg.NestedFieldPolicies)
            PrintNested(nav, policy, "  ", new HashSet<string>());

        Console.WriteLine("\nExcluded:");
        foreach (var f in cfg.ExcludedFields)
            Console.WriteLine($" - {f}");
    }

    private static void PrintNested(string nav, NestedFieldPolicy policy, string indent, HashSet<string> visited)
    {
        if (!visited.Add(nav))
        {
            Console.WriteLine($"{indent}{nav} (circular)");
            return;
        }

        Console.WriteLine($"{indent}{nav}");

        foreach (var f in policy.AllowedFields)
            Console.WriteLine($"{indent}  - {f}");

        foreach (var (childNav, childPolicy) in policy.Children)
            PrintNested(childNav, childPolicy, indent + "  ", visited);
    }
}


/*───────────────────────────────────────────────*/
/*   Debug Model                                 */
/*───────────────────────────────────────────────*/

public class KeplerPolicyDebugInfo
{
    public string EntityType { get; set; } = "";
    public string PolicyName { get; set; } = "";
    public string Role { get; set; } = "";

    public List<string> AllowedFields { get; set; } = new();
    public List<string> ExcludedFields { get; set; } = new();
    public List<string> AllowedOrderByFields { get; set; } = new();
    public Dictionary<string, FilterPolicy> AllowedFilters { get; set; } = new();
    public Dictionary<string, NestedFieldPolicy> NestedFieldPolicies { get; set; } = new();

    public HashSet<string> GlobalExclusions { get; set; } = new();

}
