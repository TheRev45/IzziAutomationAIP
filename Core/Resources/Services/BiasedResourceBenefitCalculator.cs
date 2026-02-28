using IzziAutomationCore.Resources.Entities;
using ResultAndOption;

namespace IzziAutomationCore.Resources.Services;

internal sealed class BiasedResourceBenefitCalculator(float bias) : IResourceBenefitCalculator
{
    private readonly float _bias = bias;
    public float Calculate(PopulatedResource resource) => resource
        .RealCapacity()
        .Pipe(capacity => capacity * resource.Queue.Weight(_bias))
        .Pipe(nonNormalized => nonNormalized / resource.Priority);
}