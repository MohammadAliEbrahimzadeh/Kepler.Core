using Kepler.Core.Enums;

namespace Kepler.Core.Policy;

public class FilterPolicy
{
    public string PropertyName { get; set; } = "";
    public Type PropertyType { get; set; } = null!;
    public FilterOperationEnum AllowedOperations { get; set; } = FilterOperationEnum.None;
}
