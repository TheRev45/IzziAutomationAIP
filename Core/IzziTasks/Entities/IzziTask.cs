using IzziAutomationCore.Queues.Entities;

namespace IzziAutomationCore.IzziTasks.Entities;

public sealed record IzziTask(
    Guid Id,
    IzziQueue Queue,
    DateTimeOffset Loaded,
    int Attempt,
    TimeSpan AttemptWorkTime,
    int Priority
);