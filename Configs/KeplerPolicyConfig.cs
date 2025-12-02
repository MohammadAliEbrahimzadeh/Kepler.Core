using System;

public class KeplerPolicyConfig
{
    public KeplerPolicyConfig()
    {
        IgnoreGlobalExceptions = false;

        Role = "Default";

        ReturnLambdaExpression = false;
    }


    public object? Filters { get; set; }

    public bool IgnoreGlobalExceptions { get; set; }

    public string Role { get; set; }

    public string? PolicyName { get; set; }

    public bool ReturnLambdaExpression { get; set; }
}
