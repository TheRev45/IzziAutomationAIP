using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationCore.Resources.Services;

namespace IzziAutomationSimulator;

/// <summary>
/// ICompatibleQueueChecker implementation for simulation.
///
/// All resources are considered compatible with all queues.
/// The ResourceState command generation (Login/Logout sequences in Core) already
/// handles user-switching correctly, so no pre-filtering is needed here.
/// </summary>
internal sealed class SimCompatibleQueueChecker : ICompatibleQueueChecker
{
    public bool AreCompatible(IzziResource resource, IzziQueue queue) => true;
}
