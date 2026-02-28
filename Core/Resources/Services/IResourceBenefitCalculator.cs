using IzziAutomationCore.Resources.Entities;

namespace IzziAutomationCore.Resources.Services;

public interface IResourceBenefitCalculator
{
    float Calculate(PopulatedResource resource);
}