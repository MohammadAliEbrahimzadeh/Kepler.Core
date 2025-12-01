using Kepler.Core.Attributes.Attributes;
using Kepler.Core.Builder;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;


public static class KeplerServiceExtensions
{
    public static IServiceCollection AddKepler(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>
    /// Register a single typed policy
    /// </summary>
    public static IServiceCollection AddKeplerPolicy<T, TPolicy>(this IServiceCollection services)
        where T : class
        where TPolicy : IKeplerPolicy<T>, new()
    {
        var policy = new TPolicy();
        var builder = new KeplerPolicyBuilder<T>();
        policy.Configure(builder);

        var policyName = GetPolicyName(typeof(TPolicy));
        var builtPolicies = builder.Build();
        var exclusions = builder.GetExclusions();
        var nestedPolicies = builder.GetNestedPolicies();
        var filters = builder.GetAllowedFilters();  // Get filters from builder
        var orderByFields = builder.GetAllowedOrderByFields();

        KeplerRegistry.RegisterPolicy<T>(
            policyName,
            builtPolicies,
            exclusions,
            nestedPolicies,
            filters,
            orderByFields); 

        return services;
    }

    /// <summary>
    /// Auto-register all policies from an assembly
    /// </summary>
    public static IServiceCollection AddKeplerPoliciesFromAssembly(
        this IServiceCollection services,
        Assembly assembly)
    {
        var policyInterfaceType = typeof(IKeplerPolicy<>);

        var policyTypes = assembly.GetTypes()
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType &&
                       i.GetGenericTypeDefinition() == policyInterfaceType))
            .ToList();

        if (!policyTypes.Any())
            throw new InvalidOperationException($"No Kepler policies found in assembly '{assembly.GetName().Name}'");

        foreach (var policyType in policyTypes)
        {
            var genericInterface = policyType.GetInterfaces()
                .First(i => i.IsGenericType &&
                           i.GetGenericTypeDefinition() == policyInterfaceType);

            var entityType = genericInterface.GetGenericArguments()[0];

            var registerMethod = typeof(KeplerServiceExtensions)
                .GetMethod(nameof(RegisterPolicyReflection),
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.MakeGenericMethod(entityType, policyType);

            registerMethod?.Invoke(null, new[] { services });
        }

        return services;
    }

    private static void RegisterPolicyReflection<T, TPolicy>(IServiceCollection services)
        where T : class
        where TPolicy : IKeplerPolicy<T>, new()
    {
        services.AddKeplerPolicy<T, TPolicy>();
    }

    private static string GetPolicyName(Type policyType)
    {
        var attribute = policyType.GetCustomAttribute<KeplerPolicyNameAttribute>();
        if (attribute != null)
            return attribute.Name;

        return policyType.Name.Replace("Policy", "");
    }

    /// <summary>
    /// Validate that all implemented policies are registered
    /// </summary>
    public static IServiceCollection ValidateKeplerPolicies(this IServiceCollection services)
    {
        var assembly = Assembly.GetEntryAssembly() ?? Assembly.GetCallingAssembly();

        var allAssemblies = new[] { assembly }
            .Concat(assembly.GetReferencedAssemblies().Select(Assembly.Load))
            .Where(a => a != null)
            .ToList();

        var policyInterfaceType = typeof(IKeplerPolicy<>);
        var implementedPolicies = allAssemblies
            .SelectMany(a => a.GetTypes())
            .Where(t => t.IsClass && !t.IsAbstract &&
                   t.GetInterfaces().Any(i =>
                       i.IsGenericType &&
                       i.GetGenericTypeDefinition() == policyInterfaceType))
            .ToList();

        if (!implementedPolicies.Any())
            return services;

        var registeredPolicies = KeplerRegistry.GetAllRegisteredPolicies();
        var unregisteredPolicies = new List<string>();

        foreach (var policyType in implementedPolicies)
        {
            var policyName = GetPolicyName(policyType);
            var genericInterface = policyType.GetInterfaces()
                .First(i => i.IsGenericType &&
                           i.GetGenericTypeDefinition() == policyInterfaceType);

            var entityType = genericInterface.GetGenericArguments()[0];
            var key = $"{entityType.Name}:{policyName}";

            if (!registeredPolicies.Contains(key))
            {
                unregisteredPolicies.Add(
                    $"  - {policyType.Name} (registers as '{policyName}' for {entityType.Name})");
            }
        }

        if (unregisteredPolicies.Any())
        {
            throw new InvalidOperationException(
                $"Kepler Policy Validation Failed: {unregisteredPolicies.Count} policy(ies) " +
                $"found but not registered.\n\n" +
                $"Unregistered policies:\n" +
                string.Join("\n", unregisteredPolicies) +
                $"\n\nMake sure to register them in Program.cs:\n" +
                $"services.AddKepler()\n" +
                $"    .AddKeplerPolicy<YourEntity, YourPolicy>();");
        }

        return services;
    }
}