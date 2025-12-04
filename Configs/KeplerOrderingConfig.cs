using Kepler.Core.Enums;

namespace Kepler.Core;

/// <summary>
/// Configuration for Kepler ordering with debug support
/// </summary>
public class KeplerOrderingConfig
{
    public KeplerOrderingConfig()
    {
        Direction = OrderOperationEnum.Ascending;
        Role = "Default";
        ReturnSqlQueryGenerated = false;
    }

    public string PolicyName { get; set; } = "";
    public string FieldName { get; set; } = "";
    public OrderOperationEnum Direction { get; set; }
    public string Role { get; set; }
    public bool ReturnSqlQueryGenerated { get; set; }

    /// <summary>
    /// Create an ordering config (ascending by default)
    /// </summary>
    public static KeplerOrderingConfig Create(
        string policyName,
        string fieldName,
        OrderOperationEnum direction = OrderOperationEnum.Ascending,
        string role = "Default")
    {
        return new KeplerOrderingConfig
        {
            PolicyName = policyName,
            FieldName = fieldName,
            Direction = direction,
            Role = role
        };
    }

    /// <summary>
    /// Create an ordering config with SQL visibility
    /// </summary>
    public static KeplerOrderingConfig CreateWithSql(
        string policyName,
        string fieldName,
        OrderOperationEnum direction = OrderOperationEnum.Ascending,
        string role = "Default")
    {
        return new KeplerOrderingConfig
        {
            PolicyName = policyName,
            FieldName = fieldName,
            Direction = direction,
            Role = role,
            ReturnSqlQueryGenerated = true
        };
    }

    /// <summary>
    /// Create ascending ordering config
    /// </summary>
    public static KeplerOrderingConfig CreateAscending(
        string policyName,
        string fieldName,
        string role = "Default")
    {
        return Create(policyName, fieldName, OrderOperationEnum.Ascending, role);
    }

    /// <summary>
    /// Create descending ordering config
    /// </summary>
    public static KeplerOrderingConfig CreateDescending(
        string policyName,
        string fieldName,
        string role = "Default")
    {
        return Create(policyName, fieldName, OrderOperationEnum.Descending, role);
    }
}