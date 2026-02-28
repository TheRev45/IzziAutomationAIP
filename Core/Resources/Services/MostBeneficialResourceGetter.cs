using IzziAutomationCore.Benefits.Entities;
using IzziAutomationCore.Benefits.Rules;
using IzziAutomationCore.Resources.Entities;
using ResultAndOption;

namespace IzziAutomationCore.Resources.Services;

public sealed class MostBeneficialResourceGetter(
    IComparer<ResourceWithBenefit> comparer,
    IResourceBenefitCalculator calculator,
    IBenefitRule rule) : IMostBeneficialResourceGetter
{
    private readonly IComparer<ResourceWithBenefit> _comparer = comparer;
    private readonly IResourceBenefitCalculator _calculator = calculator;
    private readonly IBenefitRule _rule = rule;

    public ResourceWithBenefit Maximize(IEnumerable<PopulatedResource> populatedResources) => populatedResources
        .Select(Generate)
        .OrderBy(resourceWithBenefit => resourceWithBenefit, _comparer)
        .First();

    private ResourceWithBenefit Generate(PopulatedResource resource) => _calculator
        .Calculate(resource)
        .Pipe(benefit => _rule.Apply(benefit, resource))
        .Pipe(benefit => new ResourceWithBenefit(resource, benefit));
}