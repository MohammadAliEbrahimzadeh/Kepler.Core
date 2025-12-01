using Kepler.Core.Enums;
using System;
using System.Linq;
using System.Linq.Expressions;

namespace Kepler.Core;

public static class KeplerOrderingExtensions
{
    /// <summary>
    /// Apply ordering to a query based on Kepler policy allowed fields using lambda expression
    /// </summary>
    /// <param name="query">The IQueryable to order</param>
    /// <param name="policyName">The Kepler policy name</param>
    /// <param name="orderByExpression">Lambda expression for the field to order by</param>
    /// <param name="direction">Ascending or Descending</param>
    /// <returns>Ordered query</returns>
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
    /// Apply ordering with a specific role using lambda expression
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
            throw new ArgumentNullException(nameof(orderByExpression), "Order by expression cannot be null");

        // Extract property name from lambda
        var propertyName = ExtractPropertyName(orderByExpression);

        if (string.IsNullOrWhiteSpace(propertyName))
            throw new InvalidOperationException(
                $"Could not extract property name from expression. Use simple property access like 'x => x.PropertyName'");

        // Get allowed order by fields from registry
        var allowedOrderByFields = KeplerRegistry.GetAllowedOrderByFields(typeof(T).Name, policyName);

        if (!allowedOrderByFields.Any())
            throw new InvalidOperationException(
                $"No order by fields configured for policy '{policyName}' on type '{typeof(T).Name}'");

        // Check if the specified role exists
        if (!allowedOrderByFields.TryGetValue(role, out var fieldsForRole))
        {
            var availableRoles = string.Join(", ", allowedOrderByFields.Keys);
            throw new InvalidOperationException(
                $"Role '{role}' not found in policy '{policyName}'. " +
                $"Available roles: {availableRoles}");
        }

        // Validate that the requested field is allowed for this role
        var fieldExists = fieldsForRole.FirstOrDefault(f =>
            string.Equals(f, propertyName, StringComparison.OrdinalIgnoreCase));

        if (fieldExists == null)
        {
            throw new InvalidOperationException(
                $"❌ SECURITY ERROR: Cannot order by field '{propertyName}' for role '{role}'.\n\n" +
                $"Allowed fields for ordering:\n" +
                $"  {string.Join(", ", fieldsForRole)}\n\n" +
                $"Configure in policy:\n" +
                $"    .AllowOrderBy(x => x.{propertyName})");
        }

        return ApplyOrderingInternal(query, fieldExists, direction);
    }

    /// <summary>
    /// Apply multiple order by clauses (ThenBy) using lambda expression
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
    /// Apply multiple order by clauses (ThenBy) with role using lambda expression
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
            throw new ArgumentNullException(nameof(orderByExpression), "Order by expression cannot be null");

        // Extract property name from lambda
        var propertyName = ExtractPropertyName(orderByExpression);

        if (string.IsNullOrWhiteSpace(propertyName))
            throw new InvalidOperationException(
                $"Could not extract property name from expression. Use simple property access like 'x => x.PropertyName'");

        var allowedOrderByFields = KeplerRegistry.GetAllowedOrderByFields(typeof(T).Name, policyName);

        if (!allowedOrderByFields.TryGetValue(role, out var fieldsForRole))
        {
            var availableRoles = string.Join(", ", allowedOrderByFields.Keys);
            throw new InvalidOperationException(
                $"Role '{role}' not found in policy '{policyName}'. " +
                $"Available roles: {availableRoles}");
        }

        var fieldExists = fieldsForRole.FirstOrDefault(f =>
            string.Equals(f, propertyName, StringComparison.OrdinalIgnoreCase));

        if (fieldExists == null)
        {
            throw new InvalidOperationException(
                $"❌ SECURITY ERROR: Cannot order by field '{propertyName}' for role '{role}'.\n\n" +
                $"Allowed fields for ordering:\n" +
                $"  {string.Join(", ", fieldsForRole)}");
        }

        return ApplyThenOrderingInternal(query, fieldExists, direction);
    }

    /// <summary>
    /// Extract property name from a lambda expression
    /// Handles both direct properties and boxed properties
    /// </summary>
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

    /// <summary>
    /// Internal method to apply ordering using reflection
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
    /// Internal method to apply ThenBy ordering
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