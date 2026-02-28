using IzziAutomationCore.IzziTasks.Entities;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.ResourceStates.Entities;

namespace IzziAutomationCore.Resources.Entities;

public sealed class UnpopulatedResource(
    Guid id,
    ResourceState state,
    ResourceLoginTimes loginTimes,
    DateTimeOffset lastItemStart) : IzziResource(
    id,
    state,
    loginTimes,
    lastItemStart)
{
    public PopulatedResource Populate(IzziQueue queue, IEnumerable<IzziTask> tasks, int priority, TimeSpan discTime) =>
        new PopulatedResource(
            Id,
            State,
            LoginTimes,
            LastItemStart,
            queue,
            tasks,
            priority,
            discTime
        );
}