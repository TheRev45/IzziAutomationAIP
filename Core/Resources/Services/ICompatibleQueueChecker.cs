using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;

namespace IzziAutomationCore.Resources.Services;

public interface ICompatibleQueueChecker
{
    public bool AreCompatible(IzziResource resource, IzziQueue queue);
}