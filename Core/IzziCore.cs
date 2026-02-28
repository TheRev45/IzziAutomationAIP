using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationCore.Resources.Services;
using ResultAndOption.Options;
using ResultAndOption.Options.Extensions;


namespace IzziAutomationCore;

internal sealed class IzziCore(
    Option<TimeSpan> discTime,
    IResourceTaskRedistributer taskRedistributer,
    IResourcePopulator resourcePopulator,
    IMostBeneficialResourceGetter getter) : IIzziCore
{
    private readonly TimeSpan _discTime = discTime.Or(TimeSpan.FromSeconds(3600)); // default: 1 hora

    public IEnumerable<CommandsForResource> Run(
        IReadOnlyList<UnpopulatedResource> resources,
        IReadOnlyList<IzziQueue> relevantQueues)
    {
        List<PopulatedResource> populatedResources = resourcePopulator.Populate(resources, relevantQueues, _discTime).ToList();
        List<ResourceWithBenefit> maximumBenefits = new List<ResourceWithBenefit>(populatedResources.Count);
        while (populatedResources.Count > 0)
        {
            taskRedistributer.Redistribute(populatedResources);
            ResourceWithBenefit maxBenefit = getter.Maximize(populatedResources);
            populatedResources = populatedResources.Where(r => r.Id != maxBenefit.Resource.Id).ToList();
            IEnumerable<PopulatedResource> equalResources = populatedResources
                .Where(r => r.Priority == maxBenefit.Resource.Priority && r.Queue.Id == maxBenefit.Resource.Queue.Id);

            foreach (PopulatedResource resource in equalResources)
            {
                resource.DecreaseCapacity(maxBenefit.Resource.NumberOfTasks);
            }
            maximumBenefits.Add(maxBenefit);
        }
        
        return maximumBenefits
            .Select(benefit => benefit.Resource)
            .Select(resource => new CommandsForResource(resource, resource.State.CommandsForQueue(resource.Queue)));
    }
}