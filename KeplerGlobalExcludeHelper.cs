using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Reflection;


namespace Kepler.Core;

/// <summary>
/// Helper class to get globally excluded properties across the entire entity graph
/// Scans recursively through navigation properties, caches results, handles cycles
/// </summary>
public static class KeplerGlobalExcludeHelper
{
    private static readonly Dictionary<Type, HashSet<string>> _globalExclusionCache = new();
    private static readonly HashSet<Type> _visitedDuringScan = new();

    /// <summary>
    /// Get all globally excluded properties for a type and its entire graph
    /// Results are cached for performance; recurses through navigation properties
    /// </summary>
    public static HashSet<string> GetGloballyExcludedProperties<T>() where T : class
    {
        return GetGloballyExcludedProperties(typeof(T));
    }

    /// <summary>
    /// Get all globally excluded properties for a type and its entire graph
    /// Caches results for performance; recurses through navigation properties
    /// </summary>
    public static HashSet<string> GetGloballyExcludedProperties(Type entityType)
    {
        // Check cache first
        if (_globalExclusionCache.TryGetValue(entityType, out var cached))
        {
            return new HashSet<string>(cached, StringComparer.OrdinalIgnoreCase);
        }
        _visitedDuringScan.Clear(); // Reset for this scan
        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            ScanEntityGraph(entityType, excluded);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Kepler] ❌ Error scanning excluded properties for graph starting at {entityType.Name}: {ex.Message}");
        }
        // Cache the result
        _globalExclusionCache[entityType] = excluded;
        Console.WriteLine($"[Kepler] ✅ Cached {excluded.Count} globally excluded properties for {entityType.Name} graph");
        return excluded;
    }

    /// <summary>
    /// Recursively scan an entity type and its navigation graph for excluded props
    /// </summary>
    private static void ScanEntityGraph(Type entityType, HashSet<string> excluded)
    {
        if (_visitedDuringScan.Contains(entityType))
            return; // Cycle detected — skip
        _visitedDuringScan.Add(entityType);
        // Scan current type's properties
        ScanTypeProperties(entityType, excluded);
        // Recurse into navigation properties (collections/single)
        var navProps = entityType.GetProperties()
            .Where(p => IsNavigationProperty(p)); // Reuse your IsNavigationProperty
        foreach (var navProp in navProps)
        {
            var navType = GetNavigationType(navProp);
            if (navType != null && !IsPrimitiveOrString(navType))
            {
                ScanEntityGraph(navType, excluded); // Recurse
            }
        }
    }

    /// <summary>
    /// Scan a type's properties for [KeplerGlobalExclude] and add to excluded set
    /// </summary>
    private static void ScanTypeProperties(Type type, HashSet<string> excluded)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        // FIXED: Type-level check first (excludes ALL props)
        var typeAttrs = type.GetCustomAttributes();
        bool typeLevelExclude = false;
        string? typeReason = null;
        foreach (var attr in typeAttrs)
        {
            var attrTypeName = attr.GetType().FullName?.Split('.').Last();
            if (attrTypeName?.Equals("KeplerGlobalExcludeAttribute", StringComparison.OrdinalIgnoreCase) == true)
            {
                typeLevelExclude = true;
                var reasonProp = attr.GetType().GetProperty("Reason");
                typeReason = reasonProp?.GetValue(attr)?.ToString() ?? "Type-level global exclusion";
                break;
            }
        }
        if (typeLevelExclude)
        {
            // Exclude ALL properties
            foreach (var property in properties)
            {
                if (!excluded.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    excluded.Add(property.Name);
                    Console.WriteLine($"[Kepler] ✅ Excluded property (type-wide): {type.Name}.{property.Name} - Reason: {typeReason}");
                }
            }
            return; // Skip prop-level checks
        }
        // Prop-level checks
        foreach (var property in properties)
        {
            var attrs = property.GetCustomAttributes();
            foreach (var attr in attrs)
            {
                var attrTypeName = attr.GetType().FullName?.Split('.').Last();
                if (attrTypeName?.Equals("KeplerGlobalExcludeAttribute", StringComparison.OrdinalIgnoreCase) == true)
                {
                    if (!excluded.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                    {
                        excluded.Add(property.Name);
                        var reasonProp = attr.GetType().GetProperty("Reason");
                        var reason = reasonProp?.GetValue(attr)?.ToString() ?? "Global exclusion";
                        Console.WriteLine($"[Kepler] ✅ Excluded property: {type.Name}.{property.Name} - Reason: {reason}");
                    }
                    break;
                }
            }
        }
        if (!excluded.Any())
        {
            Console.WriteLine($"[Kepler] ℹ️ No globally excluded properties found in {type.Name}");
        }
    }

    /// <summary>
    /// Get the navigation type (element type for collections, prop type for singles)
    /// </summary>
    private static Type? GetNavigationType(PropertyInfo prop)
    {
        var propType = prop.PropertyType;
        if (propType.IsGenericType)
        {
            var genericDef = propType.GetGenericTypeDefinition();
            if (genericDef == typeof(ICollection<>) || genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(IList<>) || genericDef == typeof(List<>))
            {
                return propType.GetGenericArguments()[0];
            }
        }
        if (propType.IsArray)
        {
            return propType.GetElementType();
        }
        if (propType.IsClass && propType != typeof(string))
        {
            return propType; // Single nav
        }
        return null;
    }

    /// <summary>
    /// Check if type is primitive/string (no recursion)
    /// </summary>
    private static bool IsPrimitiveOrString(Type type)
    {
        return type.IsPrimitive || type == typeof(string) || type == typeof(decimal) || type == typeof(DateTime);
    }

    /// <summary>
    /// Get exclusion reason for a property (for debugging/logging)
    /// </summary>
    public static string? GetExclusionReason(Type entityType, string propertyName)
    {
        var property = entityType.GetProperty(propertyName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);
        if (property == null) return null;
        var attrs = property.GetCustomAttributes();
        foreach (var attr in attrs)
        {
            var attrTypeName = attr.GetType().FullName?.Split('.').Last();
            if (attrTypeName?.Equals("KeplerGlobalExcludeAttribute", StringComparison.OrdinalIgnoreCase) == true)
            {
                var reasonProp = attr.GetType().GetProperty("Reason");
                return reasonProp?.GetValue(attr)?.ToString() ?? "Global exclusion";
            }
        }
        // Check type-level
        var typeAttrs = entityType.GetCustomAttributes();
        foreach (var attr in typeAttrs)
        {
            var attrTypeName = attr.GetType().FullName?.Split('.').Last();
            if (attrTypeName?.Equals("KeplerGlobalExcludeAttribute", StringComparison.OrdinalIgnoreCase) == true)
            {
                var reasonProp = attr.GetType().GetProperty("Reason");
                return reasonProp?.GetValue(attr)?.ToString() ?? "Type-level global exclusion";
            }
        }
        return null;
    }

    /// <summary>
    /// Get all excluded properties with their reasons for a type graph
    /// </summary>
    public static Dictionary<string, string> GetAllExcludedPropertiesWithReasons(Type entityType)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        ScanEntityGraphForReasons(entityType, result);
        return result;
    }

    private static void ScanEntityGraphForReasons(Type entityType, Dictionary<string, string> result)
    {
        if (_visitedDuringScan.Contains(entityType)) return;
        _visitedDuringScan.Add(entityType);
        ScanTypePropertiesForReasons(entityType, result);
        var navProps = entityType.GetProperties().Where(p => IsNavigationProperty(p));
        foreach (var navProp in navProps)
        {
            var navType = GetNavigationType(navProp);
            if (navType != null && !IsPrimitiveOrString(navType))
            {
                ScanEntityGraphForReasons(navType, result);
            }
        }
    }

    private static void ScanTypePropertiesForReasons(Type type, Dictionary<string, string> result)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);
        var typeAttrs = type.GetCustomAttributes();
        bool typeLevelExclude = false;
        string? typeReason = null;
        foreach (var attr in typeAttrs)
        {
            var attrTypeName = attr.GetType().FullName?.Split('.').Last();
            if (attrTypeName?.Equals("KeplerGlobalExcludeAttribute", StringComparison.OrdinalIgnoreCase) == true)
            {
                typeLevelExclude = true;
                var reasonProp = attr.GetType().GetProperty("Reason");
                typeReason = reasonProp?.GetValue(attr)?.ToString() ?? "Type-level global exclusion";
                break;
            }
        }
        if (typeLevelExclude)
        {
            foreach (var property in properties)
            {
                if (!result.ContainsKey(property.Name))
                {
                    result[property.Name] = typeReason;
                }
            }
            return;
        }
        foreach (var property in properties)
        {
            var attrs = property.GetCustomAttributes();
            foreach (var attr in attrs)
            {
                var attrTypeName = attr.GetType().FullName?.Split('.').Last();
                if (attrTypeName?.Equals("KeplerGlobalExcludeAttribute", StringComparison.OrdinalIgnoreCase) == true)
                {
                    var reasonProp = attr.GetType().GetProperty("Reason");
                    result[property.Name] = reasonProp?.GetValue(attr)?.ToString() ?? "Property-level global exclusion";
                    break;
                }
            }
        }
    }

    private static bool IsNavigationProperty(PropertyInfo prop)
    {
        var propType = prop.PropertyType;
        if (propType.IsGenericType)
        {
            var genericDef = propType.GetGenericTypeDefinition();
            if (genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(List<>))
            {
                return true;
            }
        }
        if (propType.IsArray)
            return true;
        if (prop.GetCustomAttribute<ForeignKeyAttribute>() != null)
            return true;
        if (prop.GetCustomAttribute<InversePropertyAttribute>() != null)
            return true;
        return false;
    }
}