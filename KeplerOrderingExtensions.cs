using Kepler.Core.Enums;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace Kepler.Core;

public static class KeplerOrderingExtensions
{
    /// <summary>
    /// Apply ordering using KeplerOrderingConfig (simple)
    /// </summary>
    public static IQueryable<T> ApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        KeplerOrderingConfig config)
        where T : class
    {
        return ApplyKeplerOrderingInternal(query, config, out _);
    }

    /// <summary>
    /// Apply ordering with SQL visibility
    /// </summary>
    public static IQueryable<T> ApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        KeplerOrderingConfig config,
        out string? generatedSql)
        where T : class
    {
        generatedSql = null;
        var result = ApplyKeplerOrderingInternal(query, config, out _);

        if (config.ReturnSqlQueryGenerated)
        {
            try
            {
                generatedSql = result.ToQueryString();
            }
            catch (Exception ex)
            {
                generatedSql = $"[Error generating SQL: {ex.Message}]";
            }
        }

        return result;
    }

    /// <summary>
    /// Legacy support: Apply ordering using lambda expression
    /// </summary>
    public static IQueryable<T> ApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        string policyName,
        Expression<Func<T, object>> orderByExpression,
        OrderOperationEnum direction = OrderOperationEnum.Ascending)
        where T : class
    {
        return ApplyKeplerOrdering(query, policyName, "Default", orderByExpression, direction);
    }

    /// <summary>
    /// Legacy support: Apply ordering with role using lambda expression
    /// </summary>
    public static IQueryable<T> ApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        string policyName,
        string role,
        Expression<Func<T, object>> orderByExpression,
        OrderOperationEnum direction = OrderOperationEnum.Ascending)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(policyName))
            throw new ArgumentException("Policy name cannot be empty", nameof(policyName));

        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role cannot be empty", nameof(role));

        if (orderByExpression == null)
            throw new ArgumentNullException(nameof(orderByExpression));

        var propertyName = ExtractPropertyName(orderByExpression);

        if (string.IsNullOrWhiteSpace(propertyName))
            throw new InvalidOperationException(
                $"Could not extract property name from expression. Use simple property access like 'x => x.PropertyName'");

        var config = new KeplerOrderingConfig
        {
            PolicyName = policyName,
            FieldName = propertyName,
            Direction = direction,
            Role = role
        };

        return ApplyKeplerOrderingInternal(query, config, out _);
    }

    /// <summary>
    /// Apply multiple order by clauses (ThenBy) using config
    /// </summary>
    public static IQueryable<T> ThenApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        KeplerOrderingConfig config)
        where T : class
    {
        return ApplyKeplerOrderingInternal(query, config, out _, isThenBy: true);
    }

    /// <summary>
    /// Apply multiple order by clauses (ThenBy) with SQL visibility
    /// </summary>
    public static IQueryable<T> ThenApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        KeplerOrderingConfig config,
        out string? generatedSql)
        where T : class
    {
        generatedSql = null;
        var result = ApplyKeplerOrderingInternal(query, config, out _, isThenBy: true);

        if (config.ReturnSqlQueryGenerated)
        {
            try
            {
                generatedSql = result.ToQueryString();
            }
            catch (Exception ex)
            {
                generatedSql = $"[Error generating SQL: {ex.Message}]";
            }
        }

        return result;
    }

    /// <summary>
    /// Legacy support: Apply ThenBy ordering using lambda
    /// </summary>
    public static IQueryable<T> ThenApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        string policyName,
        Expression<Func<T, object>> orderByExpression,
        OrderOperationEnum direction = OrderOperationEnum.Ascending)
        where T : class
    {
        return ThenApplyKeplerOrdering(query, policyName, "Default", orderByExpression, direction);
    }

    /// <summary>
    /// Legacy support: Apply ThenBy ordering with role using lambda
    /// </summary>
    public static IQueryable<T> ThenApplyKeplerOrdering<T>(
        this IQueryable<T> query,
        string policyName,
        string role,
        Expression<Func<T, object>> orderByExpression,
        OrderOperationEnum direction = OrderOperationEnum.Ascending)
        where T : class
    {
        if (string.IsNullOrWhiteSpace(policyName))
            throw new ArgumentException("Policy name cannot be empty", nameof(policyName));

        if (string.IsNullOrWhiteSpace(role))
            throw new ArgumentException("Role cannot be empty", nameof(role));

        if (orderByExpression == null)
            throw new ArgumentNullException(nameof(orderByExpression));

        var propertyName = ExtractPropertyName(orderByExpression);

        if (string.IsNullOrWhiteSpace(propertyName))
            throw new InvalidOperationException(
                $"Could not extract property name from expression. Use simple property access like 'x => x.PropertyName'");

        var config = new KeplerOrderingConfig
        {
            PolicyName = policyName,
            FieldName = propertyName,
            Direction = direction,
            Role = role
        };

        return ApplyKeplerOrderingInternal(query, config, out _, isThenBy: true);
    }

    /// <summary>
    /// Internal: Apply ordering with validation
    /// </summary>
    private static IQueryable<T> ApplyKeplerOrderingInternal<T>(
        IQueryable<T> query,
        KeplerOrderingConfig config,
        out string? debugInfo,
        bool isThenBy = false)
        where T : class
    {
        debugInfo = null;

        if (string.IsNullOrWhiteSpace(config.PolicyName))
            throw new ArgumentException("Policy name cannot be empty");

        if (string.IsNullOrWhiteSpace(config.FieldName))
            throw new ArgumentException("Field name cannot be empty");

        // Get allowed order by fields from registry
        var allowedOrderByFields = KeplerRegistry.GetAllowedOrderByFields(typeof(T).Name, config.PolicyName);

        if (!allowedOrderByFields.Any())
            throw new InvalidOperationException(
                $"No order by fields configured for policy '{config.PolicyName}' on type '{typeof(T).Name}'");

        // Check if the specified role exists
        if (!allowedOrderByFields.TryGetValue(config.Role, out var fieldsForRole))
        {
            var availableRoles = string.Join(", ", allowedOrderByFields.Keys);
            throw new InvalidOperationException(
                $"Role '{config.Role}' not found in policy '{config.PolicyName}'. " +
                $"Available roles: {availableRoles}");
        }

        // Validate that the requested field is allowed for this role
        var fieldExists = fieldsForRole.FirstOrDefault(f =>
            string.Equals(f, config.FieldName, StringComparison.OrdinalIgnoreCase));

        if (fieldExists == null)
        {
            throw new InvalidOperationException(
                $"❌ SECURITY ERROR: Cannot order by field '{config.FieldName}' for role '{config.Role}'.\n\n" +
                $"Allowed fields for ordering:\n" +
                $"  {string.Join(", ", fieldsForRole)}\n\n" +
                $"Configure in policy:\n" +
                $"    .AllowOrderBy(x => x.{config.FieldName})");
        }

        // Apply ordering
        if (isThenBy)
        {
            return ApplyThenOrderingInternal(query, fieldExists, config.Direction);
        }
        else
        {
            return ApplyOrderingInternal(query, fieldExists, config.Direction);
        }
    }

    /// <summary>
    /// Extract property name from lambda expression
    /// </summary>
    private static string ExtractPropertyName<T>(Expression<Func<T, object>> expression) where T : class
    {
        var body = expression.Body;

        if (body is UnaryExpression unaryExpr)
            body = unaryExpr.Operand;

        if (body is MemberExpression memberExpr)
            return memberExpr.Member.Name;

        throw new InvalidOperationException(
            $"Invalid expression. Use simple property access like 'x => x.PropertyName'. Got: {expression}");
    }

    /// <summary>
    /// Apply OrderBy or OrderByDescending
    /// </summary>
    private static IQueryable<T> ApplyOrderingInternal<T>(
        IQueryable<T> query,
        string propertyName,
        OrderOperationEnum direction)
        where T : class
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, propertyName);
        var lambda = Expression.Lambda(property, parameter);

        var methodName = direction == OrderOperationEnum.Ascending ? "OrderBy" : "OrderByDescending";
        var method = typeof(Queryable).GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), property.Type);

        return (IQueryable<T>)method.Invoke(null, new object[] { query, lambda })!;
    }

    /// <summary>
    /// Apply ThenBy or ThenByDescending
    /// </summary>
    private static IQueryable<T> ApplyThenOrderingInternal<T>(
        IQueryable<T> query,
        string propertyName,
        OrderOperationEnum direction)
        where T : class
    {
        var parameter = Expression.Parameter(typeof(T), "x");
        var property = Expression.Property(parameter, propertyName);
        var lambda = Expression.Lambda(property, parameter);

        var methodName = direction == OrderOperationEnum.Ascending ? "ThenBy" : "ThenByDescending";
        var method = typeof(Queryable).GetMethods()
            .First(m => m.Name == methodName && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), property.Type);

        return (IQueryable<T>)method.Invoke(null, new object[] { query, lambda })!;
    }
}