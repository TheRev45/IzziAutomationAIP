using IzziAutomationCore.Queues.Entities;

namespace IzziAutomationCore.Resources.Entities;

public record PopulatedResourceSetupContext(
    IzziQueue CurrentQueue,
    ResourceLoginTimes LoginTimes,
    DateTimeOffset LastItemStart);