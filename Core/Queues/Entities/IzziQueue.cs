using IzziAutomationCore.IzziTasks.Entities;
using IzziAutomationDatabase.Entities;
using ResultAndOption;
using ResultAndOption.Options;

namespace IzziAutomationCore.Queues.Entities;

public sealed class IzziQueue(
    Guid id,
    string name,
    User user,
    ConfigurableQueueParameters parameters,
    TimeSpan setupTime,
    IEnumerable<IzziTask> tasks,
    QueueFinishedTaskList finishedTasks
)
{
    private Option<TimeSpan> _averageWorkTime = Option<TimeSpan>.None();
    public Guid Id { get; } = id;
    public string Name { get; init; } = name;
    public User User { get; } = user;
    public ConfigurableQueueParameters Parameters { get; } = parameters;
    public TimeSpan SetupTime { get; } = setupTime;
    public IEnumerable<IzziTask> Tasks { get; } = tasks;
    public QueueFinishedTaskList FinishedTasks { get; } = finishedTasks;

    public float Weight(float biasCriticality) =>
        Parameters.Criticality + biasCriticality * PartialWeight();

    private float PartialWeight() => FinishedTasks
        .Select(task => task.Finished - task.Loaded <= Parameters.Sla)
        .Select(success => success ? 0 : 1)
        .Pipe(successes => (float)successes.Average());
}