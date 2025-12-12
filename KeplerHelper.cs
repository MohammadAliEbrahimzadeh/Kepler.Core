using Ardalis.GuardClauses;
using Kepler.Core.Builder;
using Kepler.Core.Enums;
using Kepler.Core.Policy;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Kepler.Core;

public static class KeplerExtensions
{
    public static IQueryable<T> ApplyKepler<T>(this IQueryable<T> query, params string[] fieldNames)
        where T : class
    {
        if (fieldNames == null || fieldNames.Length == 0)
            return query;
        return SelectFields(query, fieldNames, null, null);
    }

    private static IQueryable<T> ApplyFilters<T>(IQueryable<T> query, string typeName, string policyName, string role, object filter)
        where T : class
    {
        var allowedFiltersDict = KeplerRegistry.GetAllowedFilters(typeName, policyName);
        if (!allowedFiltersDict.TryGetValue(role, out var allowedFilters))
        {
            allowedFilters = new Dictionary<string, FilterPolicy>();
        }
        var parameter = Expression.Parameter(typeof(T), "x");
        Expression? predicate = null;
        foreach (var kvp in allowedFilters)
        {
            var filterPolicy = kvp.Value;
            var propInfo = filter.GetType().GetProperty(filterPolicy.PropertyName);
            if (propInfo == null) continue;
            var value = propInfo.GetValue(filter);
            if (value == null) continue;
            Guard.Against.Null(value, nameof(filter), $"Filter value for {filterPolicy.PropertyName} cannot be null.");
            if (value.GetType() != filterPolicy.PropertyType)
                throw new ArgumentException($"Filter value type mismatch for {filterPolicy.PropertyName}: expected {filterPolicy.PropertyType.Name}, got {value.GetType().Name}");
            var memberAccess = Expression.PropertyOrField(parameter, filterPolicy.PropertyName);
            Expression? condition = null;
            // string Contains
            if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.Contains) &&
                filterPolicy.PropertyType == typeof(string))
            {
                condition = Expression.Call(
                    memberAccess,
                    typeof(string).GetMethod("Contains", new[] { typeof(string) })!,
                    Expression.Constant(value)
                );
            }
            // string StartsWith
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.StartsWith) &&
                     filterPolicy.PropertyType == typeof(string))
            {
                condition = Expression.Call(
                    memberAccess,
                    typeof(string).GetMethod("StartsWith", new[] { typeof(string) })!,
                    Expression.Constant(value)
                );
            }
            // Equals
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.Equals))
            {
                condition = Expression.Equal(memberAccess, Expression.Constant(value, filterPolicy.PropertyType));
            }
            // GreaterThan
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.GreaterThan))
            {
                condition = Expression.GreaterThan(memberAccess, Expression.Constant(value, filterPolicy.PropertyType));
            }
            // GreaterThanOrEqual
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.GreaterThanOrEqual))
            {
                condition = Expression.GreaterThanOrEqual(memberAccess, Expression.Constant(value, filterPolicy.PropertyType));
            }
            // LessThan
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.LessThan))
            {
                condition = Expression.LessThan(memberAccess, Expression.Constant(value, filterPolicy.PropertyType));
            }
            // LessThanOrEqual
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.LessThanOrEqual))
            {
                condition = Expression.LessThanOrEqual(memberAccess, Expression.Constant(value, filterPolicy.PropertyType));
            }
            // IN (list)
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.In))
            {
                if (value is System.Collections.IEnumerable rawList && value is not string)
                {
                    var elementType = Nullable.GetUnderlyingType(filterPolicy.PropertyType)
                                      ?? filterPolicy.PropertyType;
                    var containsMethod = typeof(Enumerable)
                        .GetMethods()
                        .First(m => m.Name == "Contains" && m.GetParameters().Length == 2)
                        .MakeGenericMethod(elementType);
                    var typedArray = rawList
                        .Cast<object>()
                        .Select(v => Convert.ChangeType(v, elementType))
                        .ToArray();
                    condition = Expression.Call(
                        containsMethod,
                        Expression.Constant(typedArray),
                        memberAccess
                    );
                }
            }
            // ANY (collection.Any(...))
            else if (filterPolicy.AllowedOperations.HasFlag(FilterOperationEnum.Any))
            {
                // expecting value = LambdaExpression
                if (value is LambdaExpression subFilter)
                {
                    var anyMethod = typeof(Enumerable)
                        .GetMethods()
                        .Where(m => m.Name == "Any" && m.GetParameters().Length == 2)
                        .First()
                        .MakeGenericMethod(subFilter.Parameters[0].Type);
                    condition = Expression.Call(anyMethod, memberAccess, subFilter);
                }
            }
            if (condition != null)
            {
                predicate = predicate == null ? condition : Expression.AndAlso(predicate, condition);
            }
            else
            {
                throw new InvalidOperationException($"No allowed operation matches for {filterPolicy.PropertyName} with value type {value.GetType().Name}");
            }
        }
        return predicate != null ? query.Where(Expression.Lambda<Func<T, bool>>(predicate, parameter)) : query;
    }

    public static IQueryable<T> ApplyKeplerPolicy<T>(this IQueryable<T> query, KeplerPolicyConfig keplerPolicyConfig)
        where T : class
    {
        // Delegate to overload for backward compatibility (ignores lambda).
        return ApplyKeplerPolicy(query, keplerPolicyConfig, out Expression? _);
    }

    public static IQueryable<T> ApplyKeplerPolicy<T>(this IQueryable<T> query, KeplerPolicyConfig keplerPolicyConfig, out Expression? debugLambda)
        where T : class
    {
        debugLambda = null;

        try
        {
            if (string.IsNullOrEmpty(keplerPolicyConfig.PolicyName))
                throw new Exception("Policy name cant be null");
            var policies = KeplerRegistry.GetPolicy(typeof(T).Name, keplerPolicyConfig.PolicyName);
            var exclusions = KeplerRegistry.GetExclusions(typeof(T).Name, keplerPolicyConfig.PolicyName);
            var nestedPolicies = KeplerRegistry.GetNestedPolicies(typeof(T).Name, keplerPolicyConfig.PolicyName);
            if (!policies.TryGetValue(keplerPolicyConfig.Role, out var fields))
                throw new InvalidOperationException($"Role '{keplerPolicyConfig.Role}' not found in policy '{keplerPolicyConfig.PolicyName}'");
            var globallyExcluded = keplerPolicyConfig.IgnoreGlobalExceptions ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                     : KeplerGlobalExcludeHelper.GetGloballyExcludedPropertiesIncludingEFConfig<T>();
            var allNavigationProps = GetAllNavigationProperties<T>().ToList();
            var navigationProps = fields
                .Where(f => allNavigationProps.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            var scalarFields = fields
                .Where(f => !allNavigationProps.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Where(f => !globallyExcluded.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var excludedFields = exclusions.TryGetValue(keplerPolicyConfig.Role, out var excluded)
                ? excluded
                : Enumerable.Empty<string>();
            var allExcludedFields = keplerPolicyConfig.IgnoreGlobalExceptions
                ? excludedFields.ToList()
                : excludedFields.Union(globallyExcluded, StringComparer.OrdinalIgnoreCase).ToList();
            nestedPolicies.TryGetValue(keplerPolicyConfig.Role, out var roleNestedPolicies);
            var nestedPoliciesDict = roleNestedPolicies ?? new Dictionary<string, NestedFieldPolicy>();
            var projectedQuery = SelectFieldsWithNested(
                query,
                scalarFields,
                allExcludedFields,
                navigationProps,
                nestedPoliciesDict,
                keplerPolicyConfig.IgnoreGlobalExceptions
            );
            if (keplerPolicyConfig.Filters != null)
            {
                projectedQuery = ApplyFilters(projectedQuery, typeof(T).Name, keplerPolicyConfig.PolicyName, keplerPolicyConfig.Role, keplerPolicyConfig.Filters);
            }

            // Capture the projection lambda for debugging if requested.
            if (keplerPolicyConfig.ReturnLambdaExpression)
            {
                debugLambda = ExtractProjectionLambda(projectedQuery);
            }

            return projectedQuery;
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Error applying Kepler policy '{keplerPolicyConfig.PolicyName}' for role '{keplerPolicyConfig.Role}': {ex.Message}", ex);
        }
    }


    /// <summary>
    /// Apply Kepler policy with SQL query generation
    /// Set ReturnSqlQueryGenerated = true in config to capture SQL
    /// </summary>
    public static IQueryable<T> ApplyKeplerPolicy<T>(this IQueryable<T> query, KeplerPolicyConfig keplerPolicyConfig, out string? generatedSql)
        where T : class
    {
        generatedSql = null;

        keplerPolicyConfig.ReturnSqlQueryGenerated = true;

        var result = ApplyKeplerPolicy(query, keplerPolicyConfig, out Expression? _);

        // Extract SQL if requested
        if (keplerPolicyConfig.ReturnSqlQueryGenerated)
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
    /// Apply Kepler policy with full debug information (SQL + Lambda)
    /// Set ReturnFullDebugInfo = true in config to capture both
    /// </summary>
    public static IQueryable<T> ApplyKeplerPolicy<T>(this IQueryable<T> query, KeplerPolicyConfig keplerPolicyConfig, out KeplerDebugInfo? debugInfo)
        where T : class
    {
        debugInfo = null;

        keplerPolicyConfig.ReturnLambdaExpression = true;

        var result = ApplyKeplerPolicy(query, keplerPolicyConfig, out Expression? debugLambda);

        string? generatedSql = null;
        if (keplerPolicyConfig.ReturnFullDebugInfo)
        {
            try
            {
                generatedSql = result.ToQueryString();
            }
            catch (Exception ex)
            {
                generatedSql = $"[Error generating SQL: {ex.Message}]";
            }

            // Build debug info
            debugInfo = new KeplerDebugInfo
            {
                GeneratedSql = generatedSql,
                ProjectionLambda = debugLambda
            };
        }

        return result;
    }

    private static Expression? ExtractProjectionLambda<T>(IQueryable<T> query) where T : class
    {
        var expr = query.Expression;
        // Peel off outer Where (filters) to reach the inner Select projection.
        while (expr is MethodCallExpression mce)
        {
            if (mce.Method.Name == "Select")
            {
                // Extract the lambda from the Select call (unwrap UnaryExpression if present).
                if (mce.Arguments[1] is UnaryExpression ue && ue.Operand is LambdaExpression lambda)
                {
                    return lambda;
                }
                else if (mce.Arguments[1] is LambdaExpression lambdaDirect)
                {
                    return lambdaDirect;
                }
            }
            // Continue to the source expression (Arguments[0]).
            expr = mce.Arguments[0];
        }
        return null;
    }

    private static IQueryable<T> SelectFieldsWithNested<T>(
        IQueryable<T> query,
        string[] scalarFieldNames,
        List<string>? excludeFields,
        List<string> navigationProps,
        Dictionary<string, NestedFieldPolicy> nestedPolicies,
        bool ignoreGlobalExceptions)
        where T : class
    {
        IEnumerable<string> globalExcluded = ignoreGlobalExceptions
            ? Enumerable.Empty<string>()
            : KeplerGlobalExcludeHelper.GetGloballyExcludedPropertiesIncludingEFConfig<T>();
        var allExcludedFields = excludeFields == null
            ? globalExcluded.ToList()
            : excludeFields.Union(globalExcluded, StringComparer.OrdinalIgnoreCase).ToList();
        // Remove excluded from scalar fields
        var fieldsToSelect = scalarFieldNames
            .Where(f => !allExcludedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToArray();
        if (!fieldsToSelect.Any() && !navigationProps.Any() && !nestedPolicies.Any())
            throw new ArgumentException("No fields to select after applying exclusions");
        var parameter = Expression.Parameter(typeof(T), "x");
        var properties = typeof(T).GetProperties();
        var selectedProperties = properties
            .Where(p => fieldsToSelect.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var allBindings = new List<MemberBinding>();
        allBindings.AddRange(selectedProperties
            .Select(prop => Expression.Bind(prop, Expression.Property(parameter, prop))));
        foreach (var excludeName in allExcludedFields)
        {
            var propInfo = properties.FirstOrDefault(p =>
                string.Equals(p.Name, excludeName, StringComparison.OrdinalIgnoreCase));
            if (propInfo == null) continue;
            var defaultValue = GetDefaultValueForType(propInfo.PropertyType);
            allBindings.Add(Expression.Bind(propInfo, Expression.Constant(defaultValue, propInfo.PropertyType)));
        }
        foreach (var navProp in navigationProps)
        {
            var navProperty = properties.FirstOrDefault(p =>
                string.Equals(p.Name, navProp, StringComparison.OrdinalIgnoreCase));
            if (navProperty == null) continue;
            MemberBinding? nestedBinding = null;
            if (nestedPolicies.TryGetValue(navProp, out var nestedPolicy))
            {
                // Use explicit nested policy
                nestedBinding = CreateNestedProjection(parameter, navProperty, nestedPolicy, ignoreGlobalExceptions);
            }
            else
            {
                // Create default nested policy that includes all scalar fields (except globally excluded if not ignoring)
                var defaultNested = CreateDefaultNestedPolicy(navProperty.PropertyType, navProp, ignoreGlobalExceptions);
                nestedBinding = CreateNestedProjection(parameter, navProperty, defaultNested, ignoreGlobalExceptions);
            }
            if (nestedBinding != null)
            {
                allBindings.Add(nestedBinding);
            }
            else
            {
                // Last fallback: include navigation property as-is
                allBindings.Add(Expression.Bind(navProperty, Expression.Property(parameter, navProperty)));
            }
        }
        var allNavProps = GetAllNavigationProperties<T>();
        foreach (var notIncluded in allNavProps
            .Where(np => !navigationProps.Contains(np, StringComparer.OrdinalIgnoreCase)
                         && !nestedPolicies.ContainsKey(np)))
        {
            var navPropInfo = properties.FirstOrDefault(p =>
                string.Equals(p.Name, notIncluded, StringComparison.OrdinalIgnoreCase));
            if (navPropInfo != null)
            {
                var defaultVal = GetDefaultValueForType(navPropInfo.PropertyType);
                allBindings.Add(Expression.Bind(navPropInfo, Expression.Constant(defaultVal, navPropInfo.PropertyType)));
            }
        }
        if (!allBindings.Any())
            throw new ArgumentException($"No fields configured for type {typeof(T).Name}");
        var newExpression = Expression.New(typeof(T));
        var init = Expression.MemberInit(newExpression, allBindings);
        var lambda = Expression.Lambda<Func<T, T>>(init, parameter);
        return query.Select(lambda);
    }

    private static MemberBinding? CreateNestedProjection(
        ParameterExpression parameter,
        PropertyInfo navProperty,
        NestedFieldPolicy nestedPolicy,
        bool ignoreGlobalExceptions)
    {
        var navPropertyType = navProperty.PropertyType;
        var propertyAccess = Expression.Property(parameter, navProperty);
        var isCollection = navPropertyType.IsGenericType &&
                          (navPropertyType.GetGenericTypeDefinition() == typeof(ICollection<>) ||
                           navPropertyType.GetGenericTypeDefinition() == typeof(IEnumerable<>) ||
                           navPropertyType.GetGenericTypeDefinition() == typeof(IList<>) ||
                           navPropertyType.GetGenericTypeDefinition() == typeof(List<>));
        if (isCollection)
        {
            var elementType = navPropertyType.GetGenericArguments()[0];
            return CreateCollectionProjection(navProperty, elementType, propertyAccess, nestedPolicy, ignoreGlobalExceptions);
        }
        else
        {
            return CreateSingleEntityProjection(navProperty, navPropertyType, propertyAccess, nestedPolicy, ignoreGlobalExceptions);
        }
    }

    private static MemberBinding CreateCollectionProjection(
        PropertyInfo navProperty,
        Type elementType,
        Expression collectionAccess,
        NestedFieldPolicy nestedPolicy,
        bool ignoreGlobalExceptions)
    {
        var elementParameter = Expression.Parameter(elementType, "item");
        var projectionLambda = CreateNestedElementProjection(
            elementType,
            elementParameter,
            nestedPolicy,
            ignoreGlobalExceptions);

        // FIXED: Apply Where filter if present (before Select — filters the collection/join)
        Expression filteredCollection = collectionAccess;
        if (nestedPolicy.WhereCondition != null)
        {
            // Ensure the Where lambda's parameter matches elementType (replace if needed)
            var whereLambda = nestedPolicy.WhereCondition;
            var whereParam = whereLambda.Parameters[0];
            if (whereParam.Type != elementType)
            {
                // Replace parameter with matching type (rare, but safe)
                whereLambda = new ParameterReplacer(whereParam, elementParameter).Visit(whereLambda) as LambdaExpression
                    ?? throw new InvalidOperationException("Failed to adjust Where lambda parameter type.");
            }
            var whereMethod = typeof(Enumerable)
                .GetMethods()
                .Where(m => m.Name == "Where" && m.GetParameters().Length == 2)
                .First()
                .MakeGenericMethod(elementType);
            filteredCollection = Expression.Call(whereMethod, collectionAccess, whereLambda);
        }

        // Now Select on the (potentially filtered) collection
        var selectMethod = typeof(Enumerable)
            .GetMethods()
            .Where(m => m.Name == "Select" && m.GetGenericArguments().Length == 2)
            .First();
        var selectGenericMethod = selectMethod.MakeGenericMethod(elementType, elementType);
        var selectedCollection = Expression.Call(selectGenericMethod, filteredCollection, projectionLambda);

        var toListMethod = typeof(Enumerable)
            .GetMethods()
            .First(m => m.Name == "ToList" && m.GetGenericArguments().Length == 1)
            .MakeGenericMethod(elementType);
        var listCollection = Expression.Call(toListMethod, selectedCollection);
        return Expression.Bind(navProperty, listCollection);
    }

    private static MemberBinding CreateSingleEntityProjection(
    PropertyInfo navProperty,
    Type entityType,
    Expression entityAccess,
    NestedFieldPolicy nestedPolicy,
    bool ignoreGlobalExceptions)
    {
        var entityParameter = Expression.Parameter(entityType, "entity");
        var projectionLambda = CreateNestedElementProjection(
            entityType,
            entityParameter,
            nestedPolicy,
            ignoreGlobalExceptions);

        var projectedEntity = Expression.Invoke(projectionLambda, entityAccess);

        var nullCheck = Expression.Condition(
            Expression.Equal(entityAccess, Expression.Constant(null)),
            Expression.Constant(null, entityType),
            projectedEntity
        );

        return Expression.Bind(navProperty, nullCheck);
    }

    private static LambdaExpression CreateNestedElementProjection(
        Type elementType,
        ParameterExpression elementParameter,
        NestedFieldPolicy nestedPolicy,
        bool ignoreGlobalExceptions)
    {
        var properties = elementType.GetProperties();
        var allBindings = new List<MemberBinding>();
        var globallyExcluded = ignoreGlobalExceptions
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : KeplerGlobalExcludeHelper.GetGloballyExcludedPropertiesIncludingEFConfig(elementType);
        var fieldsToInclude = new List<string>();
        if (nestedPolicy.SelectAll)
        {
            fieldsToInclude = properties
                .Where(p => !IsNavigationProperty(p))
                .Select(p => p.Name)
                .Where(f => !nestedPolicy.ExcludedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Where(f => !globallyExcluded.Contains(f))
                .ToList();
        }
        else if (nestedPolicy.AllowedFields.Any())
        {
            // Include only allowed fields, but respect exclusions
            fieldsToInclude = nestedPolicy.AllowedFields
                .Where(f => !nestedPolicy.ExcludedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Where(f => !globallyExcluded.Contains(f))
                .ToList();
        }
        else if (nestedPolicy.ExcludedFields.Any())
        {
            // Include all except excluded
            fieldsToInclude = properties
                .Where(p => !IsNavigationProperty(p))
                .Select(p => p.Name)
                .Where(f => !nestedPolicy.ExcludedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                .Where(f => !globallyExcluded.Contains(f)) // ← Exclude globally excluded (if not ignoring)
                .ToList();
        }
        else
        {
            // No explicit configuration - include all scalar fields except globally excluded (if not ignoring)
            fieldsToInclude = properties
                .Where(p => !IsNavigationProperty(p))
                .Select(p => p.Name)
                .Where(f => !globallyExcluded.Contains(f)) // ← Exclude globally excluded (if not ignoring)
                .ToList();
        }
        // Bind selected properties
        foreach (var fieldName in fieldsToInclude)
        {
            var prop = properties.FirstOrDefault(p =>
                string.Equals(p.Name, fieldName, StringComparison.OrdinalIgnoreCase));
            if (prop != null)
            {
                allBindings.Add(
                    Expression.Bind(prop, Expression.Property(elementParameter, prop)));
            }
        }
        // Bind policy-specific excluded properties to default
        foreach (var excludedField in nestedPolicy.ExcludedFields)
        {
            var prop = properties.FirstOrDefault(p =>
                string.Equals(p.Name, excludedField, StringComparison.OrdinalIgnoreCase));
            if (prop != null && !fieldsToInclude.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
            {
                var defaultValue = GetDefaultValueForType(prop.PropertyType);
                var constant = Expression.Constant(defaultValue, prop.PropertyType);
                allBindings.Add(Expression.Bind(prop, constant));
            }
        }
        if (!ignoreGlobalExceptions)
        {
            foreach (var globalExcluded in globallyExcluded)
            {
                var prop = properties.FirstOrDefault(p =>
                    string.Equals(p.Name, globalExcluded, StringComparison.OrdinalIgnoreCase));
                if (prop != null && !fieldsToInclude.Contains(prop.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var defaultValue = GetDefaultValueForType(prop.PropertyType);
                    var constant = Expression.Constant(defaultValue, prop.PropertyType);
                    allBindings.Add(Expression.Bind(prop, constant));
                }
            }
        }
        // Bind all navigation props to default (prevent loading related entities)
        foreach (var navProp in properties.Where(p => IsNavigationProperty(p)))
        {
            if (!allBindings.Any(b => ((MemberInfo)b.Member).Name == navProp.Name))
            {
                var defaultValue = GetDefaultValueForType(navProp.PropertyType);
                var constant = Expression.Constant(defaultValue, navProp.PropertyType);
                allBindings.Add(Expression.Bind(navProp, constant));
            }
        }
        if (!allBindings.Any())
            throw new ArgumentException($"No fields configured for nested type {elementType.Name}");
        var newExpression = Expression.New(elementType);
        var memberInitExpression = Expression.MemberInit(newExpression, allBindings);
        var delegateType = typeof(Func<,>).MakeGenericType(elementType, elementType);
        return Expression.Lambda(delegateType, memberInitExpression, elementParameter);
    }

    private static IEnumerable<string> GetAllNavigationProperties<T>() where T : class
    {
        return typeof(T).GetProperties()
            .Where(p => IsNavigationProperty(p))
            .Select(p => p.Name);
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
        if (prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.ForeignKeyAttribute>() != null)
            return true;
        if (prop.GetCustomAttribute<System.ComponentModel.DataAnnotations.Schema.InversePropertyAttribute>() != null)
            return true;
        if (propType.IsClass && propType != typeof(string) && !propType.IsPrimitive && propType != typeof(DateTime) && propType != typeof(decimal))
            return true;
        return false;
    }

    private static NestedFieldPolicy CreateDefaultNestedPolicy(Type navType, string navName, bool ignoreGlobalExceptions)
    {
        var globalExcludes = ignoreGlobalExceptions
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase) // Empty set if ignoring
            : KeplerGlobalExcludeHelper.GetGloballyExcludedPropertiesIncludingEFConfig(navType);
        var scalarProps = GetScalarPropertiesForType(navType).ToList();
        // Only exclude globally excluded properties if NOT ignoring
        var excluded = ignoreGlobalExceptions
            ? new List<string>()
            : scalarProps
                .Where(p => globalExcludes.Contains(p, StringComparer.OrdinalIgnoreCase))
                .ToList();
        return new NestedFieldPolicy
        {
            NavigationProperty = navName,
            AllowedFields = scalarProps.Except(excluded, StringComparer.OrdinalIgnoreCase).ToList(),
            ExcludedFields = excluded,
            SelectAll = true, // ✅ SelectAll = true to include ALL scalar fields
            MaxDepth = 1,
            NestedType = navType
        };
    }

    private static IEnumerable<string> GetScalarProperties<T>() where T : class
    {
        return typeof(T).GetProperties()
            .Where(p => !IsNavigationProperty(p))
            .Select(p => p.Name);
    }

    private static IQueryable<T> SelectFields<T>(
        IQueryable<T> query,
        string[] fieldNames,
        List<string>? excludeFields,
        List<string>? navigationProps)
        where T : class
    {
        if (fieldNames == null || fieldNames.Length == 0)
            return query;
        var fieldsToSelect = fieldNames;
        if (excludeFields != null && excludeFields.Any())
        {
            fieldsToSelect = fieldNames
                .Where(f => !excludeFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                .ToArray();
        }
        if (!fieldsToSelect.Any())
            throw new ArgumentException("No fields to select after applying exclusions");
        var parameter = Expression.Parameter(typeof(T), "x");
        var properties = typeof(T).GetProperties();
        var selectedProperties = properties
            .Where(p => fieldsToSelect.Contains(p.Name, StringComparer.OrdinalIgnoreCase))
            .ToList();
        var allBindings = new List<MemberBinding>();
        allBindings.AddRange(selectedProperties
            .Select(prop => Expression.Bind(prop, Expression.Property(parameter, prop))));
        var excludePropNames = excludeFields == null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(excludeFields, StringComparer.OrdinalIgnoreCase);
        foreach (var excludeName in excludePropNames)
        {
            var excludeProp = properties.FirstOrDefault(p =>
                string.Equals(p.Name, excludeName, StringComparison.OrdinalIgnoreCase));
            if (excludeProp != null)
            {
                var defaultValue = GetDefaultValueForType(excludeProp.PropertyType);
                var constant = Expression.Constant(defaultValue, excludeProp.PropertyType);
                allBindings.Add(Expression.Bind(excludeProp, constant));
            }
        }
        var newExpression = Expression.New(typeof(T));
        var memberInitExpression = Expression.MemberInit(newExpression, allBindings);
        var lambda = Expression.Lambda<Func<T, T>>(memberInitExpression, parameter);
        return query.Select(lambda);
    }

    private static object? GetDefaultValueForType(Type propType)
    {
        if (IsCollectionOrNavigationType(propType))
            return null;
        var underlyingType = Nullable.GetUnderlyingType(propType) ?? propType;
        if (underlyingType == typeof(string)) return null;
        if (underlyingType == typeof(DateTime)) return default(DateTime);
        if (underlyingType == typeof(DateTimeOffset)) return default(DateTimeOffset);
        if (underlyingType == typeof(int)) return 0;
        if (underlyingType == typeof(long)) return 0L;
        if (underlyingType == typeof(bool)) return false;
        if (underlyingType == typeof(Guid)) return Guid.Empty;
        if (underlyingType == typeof(decimal)) return decimal.Zero;
        return null;
    }

    private static bool IsCollectionOrNavigationType(Type propType)
    {
        if (propType.IsGenericType)
        {
            var genericDef = propType.GetGenericTypeDefinition();
            if (genericDef == typeof(ICollection<>) ||
                genericDef == typeof(IEnumerable<>) ||
                genericDef == typeof(IList<>) ||
                genericDef == typeof(List<>))
                return true;
        }
        if (propType.IsArray)
            return true;
        if (propType.IsClass && propType != typeof(string))
            return true;
        return false;
    }

    private static IEnumerable<string> GetScalarPropertiesForType(Type type)
    {
        return type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !IsNavigationProperty(p))
            .Select(p => p.Name);
    }
}

internal class ParameterReplacer : ExpressionVisitor
{
    private readonly ParameterExpression _oldParam;
    private readonly ParameterExpression _newParam;

    public ParameterReplacer(ParameterExpression oldParam, ParameterExpression newParam)
    {
        _oldParam = oldParam;
        _newParam = newParam;
    }

    protected override Expression VisitParameter(ParameterExpression node)
    {
        return node == _oldParam ? _newParam : base.VisitParameter(node);
    }
}