using IzziAutomationCore.ResourceStates.Entities;
using IzziAutomationDatabase.Entities;
using IzziAutomationDatabase.Entities.ResourceCommands;
using ResultAndOption.Options;

namespace IzziAutomationCore.Resources.Entities;

public abstract class IzziResource(
    Guid id,
    ResourceState state,
    ResourceLoginTimes loginTimes,
    DateTimeOffset lastItemStart)
{
    public Guid Id { get; } = id;
    public ResourceState State { get; } = state;
    public ResourceLoginTimes LoginTimes { get; } = loginTimes;
    public DateTimeOffset LastItemStart { get; } = lastItemStart;
}