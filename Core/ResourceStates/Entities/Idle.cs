using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationDatabase.Entities;
using IzziAutomationDatabase.Entities.ResourceCommands;
using ResultAndOption.Options;

namespace IzziAutomationCore.ResourceStates.Entities;

public sealed record Idle(User User) : ResourceState
{
    public override List<ResourceCommand> CommandsForQueue(IzziQueue queue) => User.Id == queue.User.Id
        ? [new ExecuteQueue()]
        : [new Logout(), new Login(), new ExecuteQueue()];

    public override TimeSpan SetupTimeOverhead(in PopulatedResourceSetupContext context) =>
        (User.Id == context.CurrentQueue.User.Id
            ? TimeSpan.Zero
            : context.LoginTimes.AverageLogin + context.LoginTimes.AverageLogout) + context.CurrentQueue.SetupTime;
}