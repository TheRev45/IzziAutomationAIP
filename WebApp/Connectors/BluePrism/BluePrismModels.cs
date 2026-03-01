using IzziAutomationSimulator;

namespace IzziWebApp.Connectors.BluePrism;

// ═══════════════════════════════════════════════════════════
// Raw CSV row types
// ═══════════════════════════════════════════════════════════

record BpResource(Guid ResourceId, string Name);

record BpQueue(string Ident, string Name, string ProcessName);

record BpSession(
    int             SessionNumber,
    string          ProcessName,
    string          QueueIdent,
    string          ResourceName,
    DateTimeOffset  StartDateTime,
    DateTimeOffset  EndDateTime);

record BpItem(
    string          Ident,
    string          QueueIdent,
    string          KeyValue,
    string          Status,
    int             Attempt,
    DateTimeOffset  Loaded,
    DateTimeOffset  Finished,
    double          WorkTimeSeconds,
    string          ResourceName);

record IzziQueueConfig(
    string  QueueName,
    string  QueueBpId,
    string  IzziUser,
    int     Criticality,
    int     SlaMinutes,
    double  AvgSetupTimeFallback,
    double  AvgLoginTimeFallback,
    double  AvgLogoutTimeFallback,
    double  AvgItemTimeFallback,
    int     MinResources,
    int     MaxResources,
    bool    MustRun,
    bool    ForceMax);

// ═══════════════════════════════════════════════════════════
// Output of BluePrismConnector.Load()
// ═══════════════════════════════════════════════════════════

public class SimulationLoadResult
{
    /// <summary>Initial SimulationState: bots (LoggedOut) + queues (empty PendingItems).</summary>
    public SimulationState InitialState { get; init; } = null!;

    /// <summary>MIN(BPAWorkQueueItem.loaded) − 10 minutes.</summary>
    public DateTimeOffset SimStart { get; init; }

    /// <summary>MAX(BPASession.enddatetime) + 2 hours — generous window.</summary>
    public DateTimeOffset SimEnd { get; init; }

    /// <summary>Tasks grouped by their exact loaded timestamp, sorted ascending.</summary>
    public List<ScheduledWave> TaskWaves { get; init; } = new();

    /// <summary>All entity names keyed by GUID (resources, queues, users).</summary>
    public Dictionary<Guid, string> Names { get; init; } = new();
}

/// <summary>A batch of tasks to inject into the simulation at a specific simulated time.</summary>
public record ScheduledWave(DateTimeOffset At, List<SimTask> Tasks);
