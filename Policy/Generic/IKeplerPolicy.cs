using Kepler.Core.Builder;
using Kepler.Core.Policy;


public interface IKeplerPolicy<T> : IKeplerPolicy where T : class
{
    void Configure(IKeplerPolicyBuilder<T> builder);
}