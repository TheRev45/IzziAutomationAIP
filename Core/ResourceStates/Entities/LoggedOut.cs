using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationDatabase.Entities.ResourceCommands;

namespace IzziAutomationCore.ResourceStates.Entities;

public sealed record LoggedOut : ResourceState
{
    public override List<ResourceCommand> CommandsForQueue(IzziQueue queue) => [new Login(), new ExecuteQueue()];

    public override TimeSpan SetupTimeOverhead(in PopulatedResourceSetupContext context) => context.LoginTimes.AverageLogin + context.CurrentQueue.SetupTime;
}