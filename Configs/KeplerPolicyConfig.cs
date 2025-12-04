using System;

public class KeplerPolicyConfig
{
    public KeplerPolicyConfig()
    {
        IgnoreGlobalExceptions = false;
        Role = "Default";
        ReturnLambdaExpression = false;
        ReturnFullDebugInfo = false;
        ReturnSqlQueryGenerated = false;
    }

    public object? Filters { get; set; }
    public bool IgnoreGlobalExceptions { get; set; }
    public string Role { get; set; }
    public string? PolicyName { get; set; }
    public bool ReturnLambdaExpression { get; set; }
    public bool ReturnFullDebugInfo { get; set; }
    public bool ReturnSqlQueryGenerated { get; set; }

    /// <summary>
    /// Create a config with lambda expression debugging enabled
    /// </summary>
    public static KeplerPolicyConfig CreateWithLambda(string policyName, object? filters = null, bool ignoreGlobalExceptions = false, string role = "Default")
    {
        return new KeplerPolicyConfig
        {
            PolicyName = policyName,
            Role = role,
            ReturnLambdaExpression = true,
            Filters = filters,
            IgnoreGlobalExceptions = ignoreGlobalExceptions,
        };
    }

    /// <summary>
    /// Create a config with SQL query generation enabled
    /// </summary>
    public static KeplerPolicyConfig CreateWithSql(string policyName, object? filters = null, bool ignoreGlobalExceptions = false, string role = "Default")
    {
        return new KeplerPolicyConfig
        {
            PolicyName = policyName,
            Role = role,
            ReturnSqlQueryGenerated = true,
            Filters = filters,
            IgnoreGlobalExceptions = ignoreGlobalExceptions,
        };
    }

    /// <summary>
    /// Create a config with full debug info (SQL + Lambda)
    /// </summary>
    public static KeplerPolicyConfig CreateWithFullDebug(string policyName, object? filters = null, bool ignoreGlobalExceptions = false, string role = "Default")
    {
        return new KeplerPolicyConfig
        {
            PolicyName = policyName,
            Role = role,
            ReturnFullDebugInfo = true,
            Filters = filters,
            IgnoreGlobalExceptions = ignoreGlobalExceptions,
        };
    }

    /// <summary>
    /// Create a basic config (no debug info)
    /// </summary>
    public static KeplerPolicyConfig Create(string policyName, object? filters = null, bool ignoreGlobalExceptions = false, string role = "Default")
    {
        return new KeplerPolicyConfig
        {
            PolicyName = policyName,
            Role = role,
            Filters = filters,
            IgnoreGlobalExceptions = ignoreGlobalExceptions,
        };
    }
}