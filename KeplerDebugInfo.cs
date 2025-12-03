using System.Linq.Expressions;

namespace Kepler.Core;

public class KeplerDebugInfo
{
    public string? GeneratedSql { get; set; }

    public Expression? ProjectionLambda { get; set; }
}
