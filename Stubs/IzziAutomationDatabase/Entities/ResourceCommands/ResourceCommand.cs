namespace IzziAutomationDatabase.Entities.ResourceCommands;

public abstract class ResourceCommand;

public sealed class ExecuteQueue : ResourceCommand;

public sealed class Login : ResourceCommand;

public sealed class Logout : ResourceCommand;

public sealed class EmptyCommand : ResourceCommand;
