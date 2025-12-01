using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Kepler.Core.Attributes.Attributes;

[AttributeUsage(AttributeTargets.Class)]
public class KeplerPolicyNameAttribute : Attribute
{
    public string Name { get; }

    public KeplerPolicyNameAttribute(string name)
    {
        Name = name;
    }
}
