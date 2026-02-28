using IzziAutomationCore.Resources.Entities;
using IzziAutomationDatabase.Entities;
using IzziAutomationDatabase.Services;

namespace IzziAutomationCore.Resources.Services;

public interface IMostBeneficialResourceGetter
{
    public ResourceWithBenefit Maximize(IEnumerable<PopulatedResource> populatedResources);
}