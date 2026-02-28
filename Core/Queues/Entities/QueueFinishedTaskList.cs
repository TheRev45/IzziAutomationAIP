using System.Collections;
using IzziAutomationDatabase.Entities;
using ResultAndOption;
using ResultAndOption.Options;

namespace IzziAutomationCore.Queues.Entities;

public sealed class QueueFinishedTaskList(List<FinishedTask> tasks) : IEnumerable<FinishedTask>
{
    private readonly List<FinishedTask> _tasks = tasks;
    private Option<TimeSpan> _averageWorkTime = Option<TimeSpan>.None();
    public IEnumerator<FinishedTask> GetEnumerator() => _tasks.GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => _tasks.GetEnumerator();

    public TimeSpan AverageTaskWorkTime()
    {
        if (_averageWorkTime.IsNone())
        {
            _averageWorkTime = CalculateAverageTaskWorkTime();
        }

        return _averageWorkTime.Data;
    }

    private TimeSpan CalculateAverageTaskWorkTime() => _tasks
        .Select(t => t.WorkTime + t.AttemptWorkTime)
        .Select(t => t.Ticks)
        .Average()
        .Pipe(value => (long)value)
        .Pipe(TimeSpan.FromTicks);
}