using Microsoft.EntityFrameworkCore.Metadata.Builders;
using System;
using System.Collections.Generic;
using System.Linq.Expressions;

namespace Kepler.Core;

/// <summary>
/// Extension methods for EntityTypeBuilder to mark properties as globally excluded
/// Alternative to [KeplerGlobalExclude] attribute - configure at EF Core level
/// </summary>
public static class KeplerEntityTypeBuilderExtensions
{
    private static readonly Dictionary<Type, HashSet<string>> _efLevelGlobalExclusions = new();

    /// <summary>
    /// Mark properties as globally excluded at EF Core configuration level
    /// These will be excluded from ALL Kepler policies for this entity
    /// </summary>
    public static EntityTypeBuilder<T> GloballyExclude<T>(
        this EntityTypeBuilder<T> builder,
        params Expression<Func<T, object>>[] properties)
        where T : class
    {
        var entityType = typeof(T);
        var propertyNames = ExtractPropertyNames(properties);

        if (!_efLevelGlobalExclusions.ContainsKey(entityType))
            _efLevelGlobalExclusions[entityType] = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var propName in propertyNames)
        {
            _efLevelGlobalExclusions[entityType].Add(propName);
            Console.WriteLine($"[Kepler] 🚫 Globally excluded (EF Core config): {entityType.Name}.{propName}");
        }

        return builder;
    }

    /// <summary>
    /// Get all EF Core level global exclusions for a type
    /// </summary>
    public static HashSet<string> GetEFLevelGlobalExclusions<T>() where T : class
    {
        return GetEFLevelGlobalExclusions(typeof(T));
    }

    /// <summary>
    /// Get all EF Core level global exclusions for a type
    /// </summary>
    public static HashSet<string> GetEFLevelGlobalExclusions(Type entityType)
    {
        if (_efLevelGlobalExclusions.TryGetValue(entityType, out var exclusions))
            return new HashSet<string>(exclusions, StringComparer.OrdinalIgnoreCase);

        return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    }

    private static List<string> ExtractPropertyNames<T>(Expression<Func<T, object>>[] expressions) where T : class
    {
        var propertyNames = new List<string>();

        foreach (var expression in expressions)
        {
            var propName = ExtractPropertyName(expression);
            if (!string.IsNullOrEmpty(propName))
                propertyNames.Add(propName);
        }

        return propertyNames;
    }

    private static string ExtractPropertyName<T>(Expression<Func<T, object>> expression) where T : class
    {
        var body = expression.Body;

        // Handle boxing (UnaryExpression for value types)
        if (body is UnaryExpression unaryExpr)
            body = unaryExpr.Operand;

        if (body is MemberExpression memberExpr)
            return memberExpr.Member.Name;

        throw new InvalidOperationException(
            $"Invalid expression. Use simple property access like 'x => x.PropertyName'. Got: {expression}");
    }
}
