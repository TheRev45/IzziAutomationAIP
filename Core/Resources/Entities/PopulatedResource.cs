using IzziAutomationCore.IzziTasks.Entities;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.ResourceStates.Entities;
using ResultAndOption;
using ResultAndOption.Options;

namespace IzziAutomationCore.Resources.Entities;

public sealed class PopulatedResource(
    Guid id,
    ResourceState state,
    ResourceLoginTimes loginTimes,
    DateTimeOffset lastItemStart,
    IzziQueue assignedQueue,
    IEnumerable<IzziTask> assignedTasks,
    int priority,
    TimeSpan discTime) : IzziResource(id, state, loginTimes, lastItemStart)
{
    private Option<int> _realCapacity;
    private readonly TimeSpan _discTime = discTime;
    public IzziQueue Queue { get; } = assignedQueue;
    public int Priority { get; } = priority;
    public int NumberOfTasks { get; private set; } = assignedTasks.Count();


    public int RealCapacity()
    {
        if (_realCapacity.IsNone())
        {
            _realCapacity = CalculateRealCapacity();
        }

        return _realCapacity.Data;
    }

    public void IncreaseCapacity(int amount) => NumberOfTasks += amount;
    public void DecreaseCapacity(int amount) => NumberOfTasks -= amount;
    public float RelativeCapacity() => Math.Min(RealCapacity() / (float)NumberOfTasks, 1);

    private int CalculateRealCapacity() => TicksPerQueueWorkTime()
        .Pipe(result => (double)result)
        .Pipe(Math.Ceiling)
        .Pipe(result => (int)result);

    private long TicksPerQueueWorkTime() => (_discTime.Ticks - CalculateSetupTime().Ticks) /
                                            Queue.FinishedTasks.AverageTaskWorkTime().Ticks;

    private TimeSpan CalculateSetupTime() => Queue.SetupTime + State.SetupTimeOverhead(this);

    public static implicit operator PopulatedResourceSetupContext(PopulatedResource resource) =>
        new(resource.Queue, resource.LoginTimes, resource.LastItemStart);
}