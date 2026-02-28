using IzziAutomationCore.IzziTasks.Entities;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using ResultAndOption;

namespace IzziAutomationCore.Resources.Services;

internal sealed class PriorityAndQueueBasedResourcePopulator(ICompatibleQueueChecker compatibilityChecker)
    : IResourcePopulator
{
    private readonly ICompatibleQueueChecker _compatibilityChecker = compatibilityChecker;

    public IEnumerable<PopulatedResource> Populate(
        IReadOnlyList<UnpopulatedResource> resources,
        IReadOnlyList<IzziQueue> queues,
        TimeSpan discTime)
    {
        List<PopulatedResource> items = new();
        foreach (UnpopulatedResource resource in resources)
        {
            foreach (IzziQueue queue in queues)
            {
                IEnumerable<int> priorities = GetPriorities(queue);
                foreach (int priority in priorities)
                {
                    if (_compatibilityChecker.AreCompatible(resource, queue))
                    {
                        PopulatedResource populatedResource = PopulateResource(queue, resource, priority, discTime);
                        items.Add(populatedResource);
                    }
                }
            }
        }

        return items;
    }

    private static IEnumerable<int> GetPriorities(IzziQueue queue) => queue
        .Tasks
        .Where(task => task.Queue.Id == queue.Id)
        .Select(task => task.Priority);

    private static PopulatedResource PopulateResource(IzziQueue queue, UnpopulatedResource unpopulatedResource,
        int priority, TimeSpan discTime) => queue
        .Tasks
        .Where(t => t.Queue.Id == queue.Id)
        .Where(t => t.Priority == priority)
        .ToList()
        .Pipe(groupedTasks => unpopulatedResource.Populate(queue, groupedTasks, priority, discTime));
}