using IzziAutomationCore.Benefits.Entities;
using IzziAutomationCore.Resources.Services;
using IzziAutomationDatabase.Entities;

namespace IzziAutomationCore.Resources.Comparers;

internal sealed class BenefitResourceComparer(IComparer<Benefit> benefitComparer) : IComparer<ResourceWithBenefit>
{
    private readonly IComparer<Benefit> _benefitComparer = benefitComparer;

    public int Compare(ResourceWithBenefit? x, ResourceWithBenefit? y) => (x, y) switch
    {
        (null, null) => 0,
        (_, null) => 1,
        (null, _) => -1,
        ({ } a, { } b) => _benefitComparer.Compare(a.Benefit, b.Benefit)
    };
}