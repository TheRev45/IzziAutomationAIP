namespace IzziAutomationCore.Queues.Entities;

public sealed class ConfigurableQueueParameters(TimeSpan sla, int criticality, int minResources, int maxResources, bool forceMax, bool mustRun)
{
    public TimeSpan Sla { get; } = sla;
    public int Criticality { get; } = criticality;
    public int MinResources { get; } = minResources;
    public int MaxResources { get; } = maxResources;
    public bool ForceMax { get; } = forceMax;
    public bool MustRun { get; } = mustRun;
}