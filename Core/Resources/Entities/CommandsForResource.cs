using IzziAutomationDatabase.Entities.ResourceCommands;

namespace IzziAutomationCore.Resources.Entities;

public sealed record CommandsForResource(
    IzziResource Resource,
    IReadOnlyList<ResourceCommand> Command
);