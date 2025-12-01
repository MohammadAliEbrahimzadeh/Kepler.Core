using Kepler.Core.Builder;
using Kepler.Core.Policy;

public static class KeplerRegistry
{
    private static readonly Dictionary<string, Dictionary<string, List<string>>> _registeredPolicies = new();
    private static readonly Dictionary<string, Dictionary<string, List<string>>> _registeredExclusions = new();
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, NestedFieldPolicy>>> _registeredNestedPolicies = new();
    private static readonly Dictionary<string, Dictionary<string, Dictionary<string, FilterPolicy>>> _registeredFilters = new();
    private static readonly Dictionary<string, Dictionary<string, List<string>>> _registeredOrderByFields = new();
    private static readonly HashSet<string> _allRegisteredPolicies = new();

    /// <summary>
    /// Register a complete policy with all its components
    /// </summary>
    public static void RegisterPolicy<T>(
        string policyName,
        Dictionary<string, List<string>> policies,
        Dictionary<string, List<string>>? exclusions = null,
        Dictionary<string, Dictionary<string, NestedFieldPolicy>>? nestedPolicies = null,
        Dictionary<string, Dictionary<string, FilterPolicy>>? filters = null, Dictionary<string, List<string>>? orderByFields = null)
        where T : class
    {
        var key = $"{typeof(T).Name}:{policyName}";

        _registeredPolicies[key] = policies;
        _allRegisteredPolicies.Add(key);

        if (exclusions != null && exclusions.Any())
            _registeredExclusions[key] = exclusions;

        if (nestedPolicies != null && nestedPolicies.Any())
            _registeredNestedPolicies[key] = nestedPolicies;

        if (filters != null && filters.Any())
            _registeredFilters[key] = filters;

        if (orderByFields != null && orderByFields.Any())
            _registeredOrderByFields[key] = orderByFields;
    }

    public static Dictionary<string, List<string>> GetAllowedOrderByFields(string typeName, string policyName)
    {
        var key = $"{typeName}:{policyName}";
        if (_registeredOrderByFields.TryGetValue(key, out var orderByFields))
            return orderByFields;

        return new Dictionary<string, List<string>>();
    }


    /// <summary>
    /// Get policies for a specific type and policy name
    /// Returns: Dictionary<role, List<fieldNames>>
    /// </summary>
    public static Dictionary<string, List<string>> GetPolicy(string typeName, string policyName)
    {
        var key = $"{typeName}:{policyName}";
        if (_registeredPolicies.TryGetValue(key, out var policy))
            return policy;

        throw new InvalidOperationException(
            $"Policy '{policyName}' not found for type '{typeName}'. " +
            $"Available policies: {string.Join(", ", GetAvailablePolicies(typeName))}");
    }

    /// <summary>
    /// Get exclusions for a specific type and policy name
    /// Returns: Dictionary<role, List<excludedFieldNames>>
    /// </summary>
    public static Dictionary<string, List<string>> GetExclusions(string typeName, string policyName)
    {
        var key = $"{typeName}:{policyName}";
        if (_registeredExclusions.TryGetValue(key, out var exclusions))
            return exclusions;

        return new Dictionary<string, List<string>>();
    }

    /// <summary>
    /// Get nested field policies for a specific type and policy name
    /// Returns: Dictionary<role, Dictionary<navigationPropertyName, NestedFieldPolicy>>
    /// </summary>
    public static Dictionary<string, Dictionary<string, NestedFieldPolicy>> GetNestedPolicies(
        string typeName,
        string policyName)
    {
        var key = $"{typeName}:{policyName}";
        if (_registeredNestedPolicies.TryGetValue(key, out var nestedPolicies))
            return nestedPolicies;

        return new Dictionary<string, Dictionary<string, NestedFieldPolicy>>();
    }

    /// <summary>
    /// Get filter policies for a specific type and policy name
    /// Returns: Dictionary<role, Dictionary<propertyName, FilterPolicy>>
    /// </summary>
    public static Dictionary<string, Dictionary<string, FilterPolicy>> GetAllowedFilters(
        string typeName,
        string policyName)
    {
        var key = $"{typeName}:{policyName}";
        if (_registeredFilters.TryGetValue(key, out var filters))
            return filters;

        return new Dictionary<string, Dictionary<string, FilterPolicy>>();
    }

    /// <summary>
    /// Get all available policy names for a type
    /// </summary>
    public static IEnumerable<string> GetAvailablePolicies(string typeName)
    {
        return _registeredPolicies.Keys
            .Where(k => k.StartsWith($"{typeName}:"))
            .Select(k => k.Split(':')[1]);
    }

    /// <summary>
    /// Get all registered policy keys (for validation)
    /// </summary>
    public static IEnumerable<string> GetAllRegisteredPolicies()
    {
        return _allRegisteredPolicies;
    }
}