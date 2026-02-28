using IzziAutomationCore.Resources.Entities;
using ResultAndOption;

namespace IzziAutomationCore.Resources.Services;

internal sealed class PriorityBasedResourceTaskRedistributer : IResourceTaskRedistributer
{
    public void Redistribute(IEnumerable<PopulatedResource> populatedResources)
    {
        Stack<PopulatedResource> stack = populatedResources
            .OrderBy(item => item.Priority)
            .Pipe(ordered => new Stack<PopulatedResource>(ordered));
        while (stack.Count > 0)
        {
            PopulatedResource first = stack.Pop();
            if (first.RelativeCapacity() >= 1)
            {
                continue;
            }

            bool hasSecond = stack.TryPop(out PopulatedResource? second);
            if (!hasSecond || second is null)
            {
                continue;
            }

            int amountToRedistribute = first.RealCapacity() - first.NumberOfTasks;
            int addedToFirst = Math.Min(amountToRedistribute, second.NumberOfTasks);
            first.IncreaseCapacity(addedToFirst);
            second.DecreaseCapacity(addedToFirst);
            if (second.NumberOfTasks > 0)
            {
                stack.Push(second);
            }
            if (first.RelativeCapacity() < 1)
            {
                stack.Push(first);
            }
        }
    }
}