using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationDatabase.Entities;
using IzziAutomationDatabase.Entities.ResourceCommands;
using ResultAndOption;

namespace IzziAutomationCore.ResourceStates.Entities;

public sealed record Working(IzziQueue Queue) : ResourceState
{
    public override List<ResourceCommand> CommandsForQueue(IzziQueue queue) => Queue.Id == queue.Id
        ? [new EmptyCommand()]
        : DifferentQueueCommands(queue);

    private List<ResourceCommand> DifferentQueueCommands(IzziQueue queue) => Queue.User.Id == queue.User.Id
        ? [new ExecuteQueue()]
        : [new Logout(), new Login(), new ExecuteQueue()];

    public override TimeSpan SetupTimeOverhead(in PopulatedResourceSetupContext context)
    {
        TimeSpan spanLastItem = (DateTime.UtcNow - context.LastItemStart);
        TimeSpan setupTime = Math
            .Max((Queue.FinishedTasks.AverageTaskWorkTime() - spanLastItem).Ticks, 0)
            .Pipe(TimeSpan.FromTicks);

        setupTime += Queue.Id != context.CurrentQueue.Id ? context.CurrentQueue.SetupTime : TimeSpan.Zero;
        setupTime += Queue.User.Id != context.CurrentQueue.User.Id
            ? context.LoginTimes.AverageLogin + context.LoginTimes.AverageLogout
            : TimeSpan.Zero;

        return setupTime;
    }
}