using Kepler.Core.Builder;
using Kepler.Core.Policy;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Kepler.Core;

/// <summary>
/// Utility for inspecting and debugging Kepler policies.
/// Used for development, testing, and documentation.
/// </summary>
public static class KeplerPolicyHelper
{
    /*───────────────────────────────────────────────*/
    /*  ALLOWED FIELDS (scalar + nested, flattened)  */
    /*───────────────────────────────────────────────*/

    private static List<string> GetAllowedFields(Type entityType, string policyName, string role = "Default")
    {
        try
        {
            var allowed = new List<string>();

            // GLOBAL EXCLUDES (attributes + EF)
            var globalExcludes = KeplerGlobalExcludeHelper
                .GetGloballyExcludedPropertiesIncludingEFConfig(entityType);

            // 1. Top-level scalar fields
            var policies = KeplerRegistry.GetPolicy(entityType.Name, policyName);
            if (policies.TryGetValue(role, out var scalar))
                allowed.AddRange(scalar);

            // 2. Nested fields (recursive, unlimited depth)
            var nested = GetNestedFieldPolicies(entityType, policyName, role);

            foreach (var (nav, nestedPolicy) in nested)
            {
                CollectNestedFields(nav, nestedPolicy, allowed, new List<string>());
            }

            // APPLY GLOBAL EXCLUDES
            allowed.RemoveAll(f =>
            {
                // scalar exclusion
                if (!f.Contains('.'))
                    return globalExcludes.Contains(f);

                // nested exclusion → ProductCostHistories.StartDate → "StartDate"
                var prop = f.Split('.').Last();
                return globalExcludes.Contains(prop);
            });

            return allowed;
        }
        catch
        {
            return new List<string>();
        }
    }

    private static void CollectNestedFields(string path, NestedFieldPolicy policy, List<string> allowed, List<string> visited)
    {
        // Prevent circular references
        if (visited.Contains(path))
            return;
        visited.Add(path);

        // Add fields at current level
        foreach (var field in policy.AllowedFields)
        {
            var full = $"{path}.{field}";
            if (!allowed.Contains(full, StringComparer.OrdinalIgnoreCase))
                allowed.Add(full);
        }

        // Recursively collect ThenInclude fields
        foreach (var (thenNav, thenPolicy) in policy.ThenIncludePolicies)
        {
            var newPath = $"{path}.{thenNav}";
            CollectNestedFields(newPath, thenPolicy, allowed, new List<string>(visited));
        }
    }



    /*───────────────────────────────────────────────*/
    /*  EXCLUDED FIELDS                              */
    /*───────────────────────────────────────────────*/

    private static List<string> GetExcludedFields(Type entityType, string policyName, string role = "Default")
    {
        try
        {
            var exclusions = KeplerRegistry.GetExclusions(entityType.Name, policyName);
            return exclusions.TryGetValue(role, out var fields)
                ? new List<string>(fields)
                : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }


    /*───────────────────────────────────────────────*/
    /*  ORDER BY FIELDS                              */
    /*───────────────────────────────────────────────*/

    private static List<string> GetAllowedOrderByFields(Type entityType, string policyName, string role = "Default")
    {
        try
        {
            var orderBy = KeplerRegistry.GetAllowedOrderByFields(entityType.Name, policyName);
            return orderBy.TryGetValue(role, out var fields)
                ? new List<string>(fields)
                : new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }


    /*───────────────────────────────────────────────*/
    /*  FILTER POLICIES                              */
    /*───────────────────────────────────────────────*/

    private static Dictionary<string, FilterPolicy> GetAllowedFilters(Type entityType, string policyName, string role = "Default")
    {
        try
        {
            var filters = KeplerRegistry.GetAllowedFilters(entityType.Name, policyName);
            return filters.TryGetValue(role, out var fieldFilters)
                ? new Dictionary<string, FilterPolicy>(fieldFilters)
                : new Dictionary<string, FilterPolicy>();
        }
        catch
        {
            return new Dictionary<string, FilterPolicy>();
        }
    }


    /*───────────────────────────────────────────────*/
    /*  NESTED FIELDS (structured, not flattened)    */
    /*───────────────────────────────────────────────*/

    private static Dictionary<string, NestedFieldPolicy> GetNestedFieldPolicies(Type entityType, string policyName, string role = "Default")
    {
        try
        {
            var nested = KeplerRegistry.GetNestedPolicies(entityType.Name, policyName);
            return nested.TryGetValue(role, out var policies)
                ? new Dictionary<string, NestedFieldPolicy>(policies)
                : new Dictionary<string, NestedFieldPolicy>();
        }
        catch
        {
            return new Dictionary<string, NestedFieldPolicy>();
        }
    }


    /*───────────────────────────────────────────────*/
    /*  DEBUG INFO                                   */
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
    /*  PRINT                                        */
    /*───────────────────────────────────────────────*/

    public static void PrintPolicyConfiguration(Type entityType, string policyName, string role = "Default")
    {
        var cfg = GetPolicyConfiguration(entityType, policyName, role);

        Console.WriteLine($"================ Kepler Policy: {cfg.EntityType} :: {cfg.PolicyName} ({cfg.Role}) ================\n");

        // Scalar fields (exclude flattened nested)
        var scalarFields = cfg.AllowedFields
            .Where(f => !f.Contains('.'))
            .ToList();

        if (scalarFields.Any())
        {
            Console.WriteLine("✔ Allowed Scalar Fields:");
            foreach (var f in scalarFields)
                Console.WriteLine($"   - {f}");
        }

        // Nested fields (recursive, unlimited depth)
        if (cfg.NestedFieldPolicies.Any())
        {
            Console.WriteLine("\n🔗 Nested Fields:");
            foreach (var (nav, policy) in cfg.NestedFieldPolicies)
            {
                PrintNestedField(nav, policy, "", new List<string>());
            }
        }

        if (cfg.GlobalExclusions.Any())
        {
            Console.WriteLine("\n🚫 Global Exclusions (attribute + EF):");
            foreach (var f in cfg.GlobalExclusions)
                Console.WriteLine($"   - {f}");
        }


        if (cfg.ExcludedFields.Any())
        {
            Console.WriteLine("\n❌ Excluded Fields:");
            foreach (var f in cfg.ExcludedFields)
                Console.WriteLine($"   - {f}");
        }

        if (cfg.AllowedOrderByFields.Any())
        {
            Console.WriteLine("\n↕️ OrderBy Fields:");
            foreach (var f in cfg.AllowedOrderByFields)
                Console.WriteLine($"   - {f}");
        }

        if (cfg.AllowedFilters.Any())
        {
            Console.WriteLine("\n🔍 Filter Policies:");
            foreach (var (field, pol) in cfg.AllowedFilters)
                Console.WriteLine($"   - {field}: {string.Join(", ", pol.AllowedOperations)}");
        }

        Console.WriteLine("\n===========================================================================");
    }

    public static void PrintPolicyConfiguration<T>(string policyName, string role = "Default") where T : class =>
        PrintPolicyConfiguration(typeof(T), policyName, role);

    private static void PrintNestedField(string nav, NestedFieldPolicy policy, string indent, List<string> visited)
    {
        // Prevent circular references
        if (visited.Contains(nav))
        {
            Console.WriteLine($"{indent}   - {nav}: (circular reference, skipped)");
            return;
        }
        visited.Add(nav);

        Console.WriteLine($"{indent}   - {nav}:");
        foreach (var f in policy.AllowedFields)
            Console.WriteLine($"{indent}      • {nav}.{f}");

        if (policy.WhereCondition != null)
            Console.WriteLine($"{indent}      🔍 Has condition");

        // Recursively print ThenInclude fields
        if (policy.ThenIncludePolicies.Any())
        {
            Console.WriteLine($"{indent}      ↳ ThenInclude:");
            foreach (var (thenNav, thenPolicy) in policy.ThenIncludePolicies)
            {
                PrintNestedField(thenNav, thenPolicy, indent + "   ", new List<string>(visited));
            }
        }
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
