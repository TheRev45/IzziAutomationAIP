using IzziAutomationCommonAbstractions.Filtering;
using IzziAutomationCommonAbstractions.Identification;
using IzziAutomationDatabase.Entities;
using IzziAutomationDatabase.Services;
using ResultAndOption;

namespace IzziAutomationCore.Queues.Services
{
    public interface IAverageWorkTimeCalculator
    {
        Task<TimeSpan> Calculate(Queue queue);
    }

    internal sealed class AverageWorkTimeCalculator(IEntityService<FinishedTask> finishedTaskService, IIdentifiableQueryFilter<FinishedTask> taskFilter) : IAverageWorkTimeCalculator
    {
        public async Task<TimeSpan> Calculate(Queue queue)
        {
            IIdentifiableQueryFilter<FinishedTask> queueFilter = new FinishedTaskOfQueueQueryFilter(queue);
            CompoundQueryFilter<FinishedTask> compoundFilter = new CompoundQueryFilter<FinishedTask>(queueFilter, taskFilter);
            List<FinishedTask> finishedTasks = await finishedTaskService.Filtered(compoundFilter);
            long workTimeSum = finishedTasks.Select(t => t.WorkTime.Ticks).Sum();
            return (workTimeSum / finishedTasks.Count).Pipe(TimeSpan.FromTicks);
        }
    }

    public sealed class FinishedTaskOfQueueQueryFilter(Queue queue) : IIdentifiableQueryFilter<FinishedTask>
    {
        private readonly Queue _queue = queue;
        public IQueryable<FinishedTask> FilterQuery(IQueryable<FinishedTask> query) => query.Where(ft => ft.Queue.Id == _queue.Id);

        public IdentificationInformation GetId()
        {
            throw new NotImplementedException();
        }
    }

    public sealed class CompoundQueryFilter<T> : IIdentifiableQueryFilter<T> where T : notnull
    {
        private readonly IEnumerable<IIdentifiableQueryFilter<T>> _filters;

        public CompoundQueryFilter(params IIdentifiableQueryFilter<T>[] filters)
        {
            _filters = filters;
        }

        public IQueryable<T> FilterQuery(IQueryable<T> query)
        {
            IQueryable<T> queryable = query;
            foreach(IIdentifiableQueryFilter<T> filter in _filters)
            {
                queryable = filter.FilterQuery(queryable);
            }
            return queryable;
        }

        public IdentificationInformation GetId() => _filters
            .Select(f => f.GetId())
            .ToList()
            .Pipe(ids => new CompoundIdentificationInformation(ids));
    }

    public sealed record CompoundIdentificationInformation(List<IdentificationInformation> IdentificationInformations) : IdentificationInformation;
}
