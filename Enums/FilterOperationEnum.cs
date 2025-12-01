using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kepler.Core.Enums;

[Flags]
public enum FilterOperationEnum
{
    None = 0,
    Equals = 1 << 0,
    Contains = 1 << 1,
    StartsWith = 1 << 2,
    GreaterThan = 1 << 3,
    GreaterThanOrEqual = 1 << 4,
    LessThan = 1 << 5,
    LessThanOrEqual = 1 << 6,
    In = 1 << 7,  // For lists
    Any = 1 << 8,  // For collections: .Any(subFilter)
    All = Equals | Contains | StartsWith | GreaterThan | GreaterThanOrEqual | LessThan | LessThanOrEqual | In | Any
}
