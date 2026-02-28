using IzziAutomationCore.IzziTasks.Entities;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using ResultAndOption.Results;

namespace IzziAutomationCore;

public interface IIzziCore
{
    public IEnumerable<CommandsForResource> Run(
        IReadOnlyList<UnpopulatedResource> resource,
        IReadOnlyList<IzziQueue> relevantQueues);
}