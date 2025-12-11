using Kepler.Core.Enums;
using Kepler.Core.Policy;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;

namespace Kepler.Core.Builder;

public interface IKeplerPolicyBuilder<T> where T : class
{
    IKeplerPolicyBuilder<T> AllowFields(params Expression<Func<T, object>>[] fields);
    IKeplerPolicyBuilder<T> AllowAllExcept(params Expression<Func<T, object>>[] excludeFields);
    IKeplerPolicyBuilder<T> ExcludeFields(params Expression<Func<T, object>>[] fields);
    IKeplerPolicyBuilder<T> AllowNavigationProps(params Expression<Func<T, object>>[] navigationProps);
    IKeplerPolicyBuilder<T> AllowNestedFields<TNav>(
        Expression<Func<T, IEnumerable<TNav>>> navigationExpression,
        Action<IKeplerNestedFieldBuilder<TNav>> configureNested)
        where TNav : class;
    IKeplerPolicyBuilder<T> AllowNestedFields<TNested>(
        Expression<Func<T, TNested>> navigationExpression,
        Action<IKeplerNestedFieldBuilder<TNested>> configureNested)
        where TNested : class;
    IKeplerPolicyBuilder<T> MaxDepth(int depth);
    IKeplerPolicyBuilder<T> For(string role);

    IKeplerPolicyBuilder<T> AllowOrderBy(params Expression<Func<T, object>>[] fields);

    Dictionary<string, List<string>> Build();
    Dictionary<string, List<string>> GetExclusions();
    Dictionary<string, int> GetMaxDepths();
    Dictionary<string, Dictionary<string, NestedFieldPolicy>> GetNestedPolicies();

    IKeplerPolicyBuilder<T> AllowFilter<TProp>(Expression<Func<T, TProp>> property, FilterOperationEnum allowedOperations = FilterOperationEnum.All);

    IKeplerPolicyBuilder<T> AllowFilteredNestedFields<TNav>(Expression<Func<T, IEnumerable<TNav>>> navigationExpression, Action<IKeplerNestedFieldBuilder<TNav>> configureNested) where TNav : class;
}

public interface IKeplerNestedFieldBuilder<T> where T : class
{
    IKeplerNestedFieldBuilder<T> SelectFields(params Expression<Func<T, object>>[] fields);
    IKeplerNestedFieldBuilder<T> SelectAllExcept(params Expression<Func<T, object>>[] excludeFields);
    IKeplerNestedFieldBuilder<T> ExcludeFields(params Expression<Func<T, object>>[] fields);
    IKeplerNestedFieldBuilder<T> MaxDepth(int depth);

    IKeplerNestedFieldBuilder<T> SelectFields<TElement>(params Expression<Func<TElement, object>>[] fields) where TElement : class;

    IKeplerNestedFieldBuilder<T> ExcludeFields<TElement>(params Expression<Func<TElement, object>>[] fields) where TElement : class;

    IKeplerNestedFieldBuilder<T> Where(Expression<Func<T, bool>> condition);

    NestedFieldPolicy Build();
}

public class NestedFieldPolicy
{
    public List<string> AllowedFields { get; set; } = new();
    public List<string> ExcludedFields { get; set; } = new();
    public int MaxDepth { get; set; } = int.MaxValue;
    public bool SelectAll { get; set; } = false;
    public string NavigationProperty { get; set; } = "";
    public Type NestedType { get; set; } = null!;

    public LambdaExpression? WhereCondition { get; set; }
}

public class KeplerPolicyBuilder<T> : IKeplerPolicyBuilder<T> where T : class
{
    private readonly Dictionary<string, BuilderState> _states = new();
    private string _currentRole = "Default";
    private int _currentMaxDepth = 3;  // Default max depth to prevent accidental over-fetching

    private class BuilderState
    {
        public List<string> AllowedOrderByFields { get; set; } = new();
        public Dictionary<string, FilterPolicy> AllowedFilters { get; set; } = new();

        public List<string> AllowedFields { get; set; } = new();
        public List<string> ExcludedFields { get; set; } = new();
        public List<string> AllowedNavigationProps { get; set; } = new();
        public Dictionary<string, NestedFieldPolicy> NestedFieldPolicies { get; set; } = new();
        public int MaxDepth { get; set; } = 3;  // Default to 3 levels deep
        public bool UseAllFields { get; set; } = false;
        public bool ExplicitlyAllowedNavProps { get; set; } = false;
    }

    private readonly Dictionary<string, FilterPolicy> _allowedFilters = new();

    public IKeplerPolicyBuilder<T> AllowFilteredNestedFields<TNav>(
      Expression<Func<T, IEnumerable<TNav>>> navigationExpression,
      Action<IKeplerNestedFieldBuilder<TNav>> configureNested)
      where TNav : class
    {
        // Extract navigation property name (handles x => x.Nav and x => x.Nav.Where(...))
        var navPropName = ExtractNavigationPropertyName(navigationExpression);
        // Extract predicate (if any) from the Where(...) call chain
        var whereCondition = ExtractWhereCondition<TNav>(navigationExpression);
        var nestedBuilder = new KeplerNestedFieldBuilder<TNav>(navPropName);
        // Apply where condition if found
        if (whereCondition != null)
        {
            // Cast is safe because the lambda's parameter type comes from TNav
            nestedBuilder.Where((Expression<Func<TNav, bool>>)whereCondition);
        }
        configureNested(nestedBuilder);
        var policy = nestedBuilder.Build();
        GetState().NestedFieldPolicies[navPropName] = policy;
        if (!GetState().AllowedNavigationProps.Contains(navPropName, StringComparer.OrdinalIgnoreCase))
            GetState().AllowedNavigationProps.Add(navPropName);
        GetState().ExplicitlyAllowedNavProps = true;
        return this;
    }


    public IKeplerPolicyBuilder<T> AllowOrderBy(params Expression<Func<T, object>>[] fields)
    {
        var fieldNames = ExtractPropertyNames(fields);
        var state = GetState();

        // ✅ VALIDATE: Fields must be allowed first
        var notAllowed = fieldNames
            .Where(f => !state.UseAllFields && !state.AllowedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (notAllowed.Any())
        {
            throw new InvalidOperationException(
                $"❌ SECURITY ERROR: Cannot allow ordering on fields: {string.Join(", ", notAllowed)}\n\n" +
                $"These fields must be in AllowFields() first.\n" +
                $"Fix: Add to policy:\n" +
                $"    .AllowFields(x => x.{notAllowed[0]}, ...)\n" +
                $"    .AllowOrderBy(x => x.{notAllowed[0]}, ...)");
        }

        // ✅ Add to allowed order by fields
        state.AllowedOrderByFields.AddRange(fieldNames);
        return this;
    }

    public Dictionary<string, List<string>> GetAllowedOrderByFields()
    {
        return _states
            .Where(kvp => kvp.Value.AllowedOrderByFields.Any())
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AllowedOrderByFields);
    }


    public IKeplerPolicyBuilder<T> AllowFilter<TProp>(Expression<Func<T, TProp>> property, FilterOperationEnum allowedOperations = FilterOperationEnum.All)
    {
        var propName = ExtractPropertyName(property);
        var state = GetState();  // Your BuilderState

        if (!state.UseAllFields && !state.AllowedFields.Contains(propName, StringComparer.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Property '{propName}' must be explicitly allowed in .AllowFields() or .AllowAllExcept() before filtering. " +
                $"Add it to AllowedFields or set UseAllFields=true with .AllowAllExcept().");
        }

        // Existing: Register the filter
        state.AllowedFilters[propName] = new FilterPolicy
        {
            PropertyName = propName,
            AllowedOperations = allowedOperations,
            PropertyType = typeof(TProp)
        };

        return this;
    }


    public IKeplerPolicyBuilder<T> AllowFields(params Expression<Func<T, object>>[] fields)
    {
        var fieldNames = ExtractPropertyNames(fields);
        GetState().AllowedFields.AddRange(fieldNames);
        GetState().UseAllFields = false;
        return this;
    }

    public IKeplerPolicyBuilder<T> AllowAllExcept(params Expression<Func<T, object>>[] excludeFields)
    {
        var fieldNames = ExtractPropertyNames(excludeFields);
        GetState().UseAllFields = true;
        GetState().ExcludedFields.AddRange(fieldNames);
        return this;
    }

    public IKeplerPolicyBuilder<T> ExcludeFields(params Expression<Func<T, object>>[] fields)
    {
        var fieldNames = ExtractPropertyNames(fields);
        GetState().ExcludedFields.AddRange(fieldNames);
        return this;
    }

    public IKeplerPolicyBuilder<T> AllowNavigationProps(params Expression<Func<T, object>>[] navigationProps)
    {
        var propNames = ExtractPropertyNames(navigationProps);
        var actualNavProps = GetAllNavigationProperties().ToList();

        var validNavProps = propNames
            .Where(np => actualNavProps.Contains(np, StringComparer.OrdinalIgnoreCase))
            .ToList();

        if (!validNavProps.Any())
            throw new InvalidOperationException(
                $"No valid navigation properties found. Available: {string.Join(", ", actualNavProps)}");

        GetState().AllowedNavigationProps.AddRange(validNavProps);
        GetState().ExplicitlyAllowedNavProps = true;
        return this;
    }

    public IKeplerPolicyBuilder<T> AllowNestedFields<TNav>(
        Expression<Func<T, IEnumerable<TNav>>> navigationExpression,
        Action<IKeplerNestedFieldBuilder<TNav>> configureNested)
        where TNav : class
    {
        var navPropName = ExtractPropertyName(navigationExpression);
        var nestedBuilder = new KeplerNestedFieldBuilder<TNav>(navPropName);
        configureNested(nestedBuilder);
        var policy = nestedBuilder.Build();

        GetState().NestedFieldPolicies[navPropName] = policy;

        // Auto-add to navigation props if not already there
        if (!GetState().AllowedNavigationProps.Contains(navPropName, StringComparer.OrdinalIgnoreCase))
            GetState().AllowedNavigationProps.Add(navPropName);

        GetState().ExplicitlyAllowedNavProps = true;
        return this;
    }

    public IKeplerPolicyBuilder<T> AllowNestedFields<TNested>(
        Expression<Func<T, TNested>> navigationExpression,
        Action<IKeplerNestedFieldBuilder<TNested>> configureNested)
        where TNested : class
    {
        var navPropName = ExtractPropertyName(navigationExpression);
        var nestedBuilder = new KeplerNestedFieldBuilder<TNested>(navPropName);
        configureNested(nestedBuilder);
        var policy = nestedBuilder.Build();

        GetState().NestedFieldPolicies[navPropName] = policy;

        if (!GetState().AllowedNavigationProps.Contains(navPropName, StringComparer.OrdinalIgnoreCase))
            GetState().AllowedNavigationProps.Add(navPropName);

        GetState().ExplicitlyAllowedNavProps = true;
        return this;
    }

    public IKeplerPolicyBuilder<T> MaxDepth(int depth)
    {
        if (depth < 1)
            throw new ArgumentException("MaxDepth must be at least 1", nameof(depth));

        _currentMaxDepth = depth;
        GetState().MaxDepth = depth;
        return this;
    }

    public IKeplerPolicyBuilder<T> For(string role)
    {
        _currentRole = role;
        _currentMaxDepth = 3;  // Reset to default when switching roles
        return this;
    }

    public Dictionary<string, List<string>> Build()
    {
        var result = new Dictionary<string, List<string>>();

        foreach (var kvp in _states)
        {
            var role = kvp.Key;
            var state = kvp.Value;

            List<string> finalFields;

            if (state.UseAllFields)
            {
                finalFields = GetScalarProperties().ToList();
                finalFields = finalFields
                    .Where(f => !state.ExcludedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (state.AllowedFields.Any())
            {
                finalFields = state.AllowedFields
                    .Where(f => !state.ExcludedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (state.ExcludedFields.Any())
            {
                finalFields = GetScalarProperties()
                    .Where(f => !state.ExcludedFields.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else
            {
                throw new InvalidOperationException(
                    $"Role '{role}' has no fields configured. Use AllowFields(), AllowAllExcept(), or ExcludeFields().");
            }

            if (!finalFields.Any() && !state.AllowedNavigationProps.Any() && !state.NestedFieldPolicies.Any())
                throw new InvalidOperationException($"Role '{role}' resulted in 0 fields after filtering.");

            var allNavProps = GetAllNavigationProperties().ToList();

            if (state.ExplicitlyAllowedNavProps && state.AllowedNavigationProps.Any())
            {
                finalFields.AddRange(state.AllowedNavigationProps);
                var navPropsToExclude = allNavProps
                    .Where(np => !state.AllowedNavigationProps.Contains(np, StringComparer.OrdinalIgnoreCase))
                    .ToList();

                finalFields = finalFields
                    .Where(f => !navPropsToExclude.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }
            else if (!state.ExplicitlyAllowedNavProps && !state.NestedFieldPolicies.Any())
            {
                finalFields = finalFields
                    .Where(f => !allNavProps.Contains(f, StringComparer.OrdinalIgnoreCase))
                    .ToList();
            }

            result[role] = finalFields;
        }

        return result;
    }

    public Dictionary<string, List<string>> GetExclusions()
    {
        return _states
            .Where(kvp => kvp.Value.ExcludedFields.Any())
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.ExcludedFields);
    }

    public Dictionary<string, int> GetMaxDepths()
    {
        return _states
            .Where(kvp => kvp.Value.MaxDepth < int.MaxValue)
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.MaxDepth);
    }

    public Dictionary<string, Dictionary<string, NestedFieldPolicy>> GetNestedPolicies()
    {
        return _states
            .Where(kvp => kvp.Value.NestedFieldPolicies.Any())
            .ToDictionary(kvp => kvp.Key, kvp => kvp.Value.NestedFieldPolicies);
    }

    public Dictionary<string, Dictionary<string, FilterPolicy>> GetAllowedFilters()
    {
        return _states.ToDictionary(kvp => kvp.Key, kvp => kvp.Value.AllowedFilters);
    }

    private BuilderState GetState()
    {
        if (!_states.ContainsKey(_currentRole))
            _states[_currentRole] = new BuilderState();
        return _states[_currentRole];
    }

    private IEnumerable<string> GetAllNavigationProperties()
    {
        return typeof(T).GetProperties()
            .Where(p => IsNavigationProperty(p))
            .Select(p => p.Name);
    }

    private bool IsNavigationProperty(PropertyInfo prop)
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

        return false;
    }

    private IEnumerable<string> GetScalarProperties()
    {
        return typeof(T).GetProperties()
            .Where(p => !IsNavigationProperty(p))
            .Select(p => p.Name);
    }

    private List<string> ExtractPropertyNames(Expression<Func<T, object>>[] expressions)
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




    private string ExtractPropertyName<TExpr>(Expression<Func<T, TExpr>> expression)
    {
        var body = expression.Body;

        if (body is UnaryExpression unaryExpr)
            body = unaryExpr.Operand;

        if (body is MemberExpression memberExpr)
            return memberExpr.Member.Name;

        throw new InvalidOperationException(
            $"Invalid expression. Use simple property access like 'x => x.PropertyName'. Got: {expression}");
    }

    // Robust helper to extract navigation property name from expressions like:
    // - x => x.Collection
    // - x => x.Collection.Where(...)
    // - x => x.Collection.Where(...).Where(...)  (walks to root)
    private string ExtractNavigationPropertyName<TNav>(
        Expression<Func<T, IEnumerable<TNav>>> navigationExpression)
        where TNav : class
    {
        Expression body = navigationExpression.Body;

        // unwrap conversions
        if (body is UnaryExpression unary)
            body = unary.Operand;

        // direct member: x => x.Collection
        if (body is MemberExpression member)
            return member.Member.Name;

        // method chain: x => x.Collection.Where(...).Where(...)
        if (body is MethodCallExpression call)
        {
            // Find the root source expression (member) by walking down the call chain
            Expression? source = call.Object ?? (call.Arguments.Count > 0 ? call.Arguments[0] : null);

            while (source is MethodCallExpression innerCall)
            {
                source = innerCall.Object ?? (innerCall.Arguments.Count > 0 ? innerCall.Arguments[0] : null);
            }

            if (source is MemberExpression rootMember)
                return rootMember.Member.Name;

            // static Enumerable.Where(source, predicate) pattern: first arg could be MemberExpression
            if (call.Arguments.FirstOrDefault() is MemberExpression argMember)
                return argMember.Member.Name;
        }

        // fallback to generic extractor (this will throw helpful message if not a simple property)
        return ExtractPropertyName(navigationExpression);
    }

    // Robust helper to extract the first Where(...) predicate from a navigation expression chain.
    // Supports both Queryable.Where and Enumerable.Where static call forms and chained calls.
    private LambdaExpression? ExtractWhereCondition<TNav>(
        Expression<Func<T, IEnumerable<TNav>>> navigationExpression)
        where TNav : class
    {
        Expression body = navigationExpression.Body;

        // unwrap conversions
        if (body is UnaryExpression unary)
            body = unary.Operand;

        // Walk method-call chain to find the first Where(...)
        MethodCallExpression? whereCall = FindWhereCall(body as Expression);

        if (whereCall == null)
            return null;

        // Where usually has predicate as the second argument
        if (whereCall.Arguments.Count < 2)
            return null;

        var predicateArg = whereCall.Arguments[1];

        // If quoted (Expression Quote), unwrap
        if (predicateArg is UnaryExpression quote && quote.NodeType == ExpressionType.Quote && quote.Operand is LambdaExpression lam)
            return lam;

        if (predicateArg is LambdaExpression lam2)
            return lam2;

        return null;
    }

    // helper that walks expression to locate a MethodCallExpression where Method.Name == "Where"
    private MethodCallExpression? FindWhereCall(Expression? expr)
    {
        while (expr != null)
        {
            if (expr is MethodCallExpression call)
            {
                if (string.Equals(call.Method.Name, "Where", StringComparison.OrdinalIgnoreCase))
                    return call;

                // dive into call.Object, otherwise into first argument (static Enumerable methods)
                expr = call.Object ?? (call.Arguments.Count > 0 ? call.Arguments[0] : null);
                continue;
            }

            // if it's a unary wrap, unwrap
            if (expr is UnaryExpression unary)
            {
                expr = unary.Operand;
                continue;
            }

            break;
        }

        return null;
    }
}

public class KeplerNestedFieldBuilder<T> : IKeplerNestedFieldBuilder<T> where T : class
{
    private Expression<Func<T, bool>>? _whereCondition;
    private readonly string _navigationProperty;
    private List<string> _allowedFields = new();
    private List<string> _excludedFields = new();
    private int _maxDepth = int.MaxValue;
    private bool _selectAll = false;

    public KeplerNestedFieldBuilder(string navigationProperty)
    {
        _navigationProperty = navigationProperty;
    }

    public IKeplerNestedFieldBuilder<T> Where(Expression<Func<T, bool>> condition)
    {
        _whereCondition = condition;
        return this;
    }


    public IKeplerNestedFieldBuilder<T> SelectFields<TElement>(params Expression<Func<TElement, object>>[] fields) where TElement : class
    {
        _allowedFields.AddRange(
            fields.Select(f => ExtractPropertyNameForElement(f))
        );
        return this;
    }

    public IKeplerNestedFieldBuilder<T> ExcludeFields<TElement>(params Expression<Func<TElement, object>>[] fields) where TElement : class
    {
        _excludedFields.AddRange(
            fields.Select(f => ExtractPropertyNameForElement(f))
        );
        return this;
    }

    private string ExtractPropertyNameForElement<TElement>(Expression<Func<TElement, object>> expr)
    {
        var body = expr.Body;
        if (body is UnaryExpression u)
            body = u.Operand;

        if (body is MemberExpression m)
            return m.Member.Name;

        throw new InvalidOperationException($"Invalid expression: {expr}");
    }



    // ✅ FIXED: Use object instead of generic TProp
    public IKeplerNestedFieldBuilder<T> SelectFields(params Expression<Func<T, object>>[] fields)
    {
        _allowedFields.AddRange(ExtractPropertyNames(fields));
        _selectAll = false;
        return this;
    }

    public IKeplerNestedFieldBuilder<T> SelectAllExcept(params Expression<Func<T, object>>[] excludeFields)
    {
        _selectAll = true;
        _excludedFields.AddRange(ExtractPropertyNames(excludeFields));
        return this;
    }

    public IKeplerNestedFieldBuilder<T> ExcludeFields(params Expression<Func<T, object>>[] fields)
    {
        _excludedFields.AddRange(ExtractPropertyNames(fields));
        return this;
    }

    public IKeplerNestedFieldBuilder<T> MaxDepth(int depth)
    {
        if (depth < 1)
            throw new ArgumentException("MaxDepth must be at least 1", nameof(depth));
        _maxDepth = depth;
        return this;
    }

    public NestedFieldPolicy Build()
    {
        if (!_allowedFields.Any() && !_selectAll && !_excludedFields.Any())
            throw new InvalidOperationException(
                $"Nested field policy for '{_navigationProperty}' has no fields configured. " +
                $"Use SelectFields(), SelectAllExcept(), or ExcludeFields().");
        return new NestedFieldPolicy
        {
            NavigationProperty = _navigationProperty,
            AllowedFields = _allowedFields,
            ExcludedFields = _excludedFields,
            MaxDepth = _maxDepth,
            SelectAll = _selectAll,
            NestedType = typeof(T),
            WhereCondition = _whereCondition  // ← Already set
        };
    }

    // ✅ Helper to extract property names from object expressions
    private List<string> ExtractPropertyNames(Expression<Func<T, object>>[] expressions)
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

    // ✅ Extract property name, handling UnaryExpression (for value types)
    private string ExtractPropertyName(Expression<Func<T, object>> expression)
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
