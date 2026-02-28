namespace IzziAutomationDatabase.Entities;

public class FinishedTask
{
    public TimeSpan WorkTime { get; init; }
    public TimeSpan AttemptWorkTime { get; init; }
    public DateTimeOffset Finished { get; init; }
    public DateTimeOffset Loaded { get; init; }
    public Queue Queue { get; init; } = null!;
}
