using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kepler.Core.Attributes;


[AttributeUsage(AttributeTargets.Property)]
public class KeplerGlobalExcludeAttribute : Attribute
{
    public string Reason { get; }

    public KeplerGlobalExcludeAttribute(string reason)
    {
        Reason = reason;
    }
}
