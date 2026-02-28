using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationDatabase.Entities.ResourceCommands;

namespace IzziAutomationCore.ResourceStates.Entities;


public abstract record ResourceState
{
    public abstract TimeSpan SetupTimeOverhead(in PopulatedResourceSetupContext context);

    public abstract List<ResourceCommand> CommandsForQueue(IzziQueue queue);
};