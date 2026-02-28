using IzziAutomationCore.Benefits.Entities;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using System.Resources;

namespace IzziAutomationCore.Benefits.Rules;

public interface IBenefitRule
{
    public Benefit Apply(float benefit, PopulatedResource resource);
}

internal sealed class MaximumNumberOfMachinesRule(IAssignedResourceForQueueAccessor accessor) : IBenefitRule
{
    public Benefit Apply(float benefit, PopulatedResource resource) => HasReachedPeak(resource)
        ? new FloatBenefit(0)
        : new FloatBenefit(benefit);

    private bool HasReachedPeak(PopulatedResource resource) => accessor.ResourcesAssignedToQueue(resource.Queue) >=
                                                               resource.Queue.Parameters.MaxResources;
}

internal sealed class MinimumNumberOfMachinesRule(IAssignedResourceForQueueAccessor accessor)
    : IBenefitRule
{
    public Benefit Apply(float benefit, PopulatedResource resource) =>
        HasMinParameters(resource) && !HasEnoughResources(resource)
            ? new InfiniteBenefit()
            : new FloatBenefit(benefit);

    private bool HasMinParameters(PopulatedResource resource) => resource.Queue.Parameters.MinResources != 0;

    private bool HasEnoughResources(PopulatedResource resource) => accessor
        .ResourcesAssignedToQueue(resource.Queue) < resource.Queue.Parameters.MinResources;
}

internal sealed class MustRunRule
    : IBenefitRule
{
    public Benefit Apply(float benefit, PopulatedResource resource) =>
        resource.Queue.Parameters.MustRun && resource.Priority == 1
            ? new InfiniteBenefit()
            : new FloatBenefit(benefit);
}

public interface IAssignedResourceForQueueAccessor
{
    public int ResourcesAssignedToQueue(IzziQueue queue);
}