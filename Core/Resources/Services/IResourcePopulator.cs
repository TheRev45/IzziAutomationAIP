using IzziAutomationCore.IzziTasks.Entities;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;

namespace IzziAutomationCore.Resources.Services;

public interface IResourcePopulator
{
    public IEnumerable<PopulatedResource> Populate(
        IReadOnlyList<UnpopulatedResource> resources,
        IReadOnlyList<IzziQueue> queues,
        TimeSpan discTime);
}