using IzziAutomationCore;

namespace IzziAutomationSimulator;

/// <summary>
/// Worker — coração do sistema de orquestração.
///
/// Responsabilidades:
///   1. Observar estado dos recursos
///   2. Detectar triggers para chamar Izzi (timer 10min OU recursos Idle)
///   3. Chamar IIzziCore.Run() para obter comandos
///   4. Traduzir comandos Core → comandos Simulator via IzziStateAdapter
///   5. Agendar eventos na EventQueue
///
/// Design: Stateless (não guarda histórico, apenas age no estado actual)
/// </summary>
public class Worker
{
    private readonly SimulationState _state;
    private readonly EventQueue _eventQueue;
    private readonly SimulationClock _clock;
    private readonly SimulatorConfiguration _config;
    private readonly IIzziCore _izziCore;

    private DateTimeOffset _lastIzziCall = DateTimeOffset.MinValue;

    public Worker(
        SimulationState state,
        EventQueue eventQueue,
        SimulationClock clock,
        SimulatorConfiguration config,
        IIzziCore izziCore)
    {
        _state = state;
        _eventQueue = eventQueue;
        _clock = clock;
        _config = config;
        _izziCore = izziCore;
    }

    /// <summary>
    /// Observa o estado e age se necessário.
    ///
    /// Triggers para chamar Izzi:
    ///   1. Timer: passaram 10 minutos desde última chamada
    ///   2. Idle: um ou mais recursos ficaram Idle sem comandos pendentes
    ///
    /// Após chamar Izzi:
    ///   - Traduz comandos Core em comandos Simulator
    ///   - Atribui comandos a cada recurso como PendingCommands
    /// </summary>
    public void Observe()
    {
        // Trigger 1: Timer (10 minutos)
        var timeSinceLastCall = _clock.Now - _lastIzziCall;
        bool timerTrigger = timeSinceLastCall >= _config.IzziTimerInterval;

        // Trigger 2: Recursos Idle sem comandos
        bool idleTrigger = _state.Resources.Values.Any(r =>
            r.CurrentState == SimResourceState.Idle &&
            r.PendingCommands.Count == 0
        );

        if (timerTrigger || idleTrigger)
        {
            CallIzzi();
            _lastIzziCall = _clock.Now;
        }

        ExecutePendingCommands();
    }

    /// <summary>
    /// Chama IIzziCore.Run() para obter comandos de decisão e converte-os
    /// para comandos internos do Simulator via IzziStateAdapter.
    ///
    /// Fluxo:
    ///   1. Converte SimulationState → Core types (UnpopulatedResource[], IzziQueue[])
    ///   2. Chama IIzziCore.Run()
    ///   3. Traduz CommandsForResource[] → Simulator pending commands por recurso
    ///   4. Atribui PendingCommands a cada recurso
    /// </summary>
    private void CallIzzi()
    {
        var (resources, queues) = IzziStateAdapter.ToCore(_state);
        var coreCommands        = _izziCore.Run(resources, queues);
        var commandsByResource  = IzziStateAdapter.ToSimCommands(coreCommands);

        foreach (var (resourceId, commands) in commandsByResource)
        {
            if (_state.Resources.TryGetValue(resourceId, out var resource))
            {
                resource.PendingCommands = commands;
            }
        }
    }

    /// <summary>
    /// Executa comandos pendentes de recursos que estão Idle/LoggedOut.
    ///
    /// Regra: Só executar comandos quando recurso está num estado "estável":
    ///   - LoggedOut → pode executar Login
    ///   - Idle → pode executar StartProcess, Logout, etc
    ///   - Working → NÃO pode executar (aguarda ficar Idle)
    /// </summary>
    private void ExecutePendingCommands()
    {
        foreach (var resource in _state.Resources.Values)
        {
            if (resource.PendingCommands.Count == 0)
                continue;

            bool canExecute = resource.CurrentState == SimResourceState.LoggedOut ||
                              resource.CurrentState == SimResourceState.Idle;

            if (!canExecute)
                continue;

            var nextCmd = resource.PendingCommands[0];
            resource.PendingCommands.RemoveAt(0);

            ExecuteCommand(nextCmd, resource);
        }
    }

    /// <summary>
    /// Executa um comando específico (agenda eventos correspondentes).
    ///
    /// Comandos possíveis:
    ///   - LoginCommand(userId)     → LoginCompletedEvent após AvgLoginTime
    ///   - LogoutCommand()          → LogoutCompletedEvent após AvgLogoutTime
    ///   - StartProcessCommand(id)  → SetupCompletedEvent após AvgSetupTime
    ///   - StopProcessCommand()     → marca RequestStopAt (paragem passiva)
    /// </summary>
    private void ExecuteCommand(object cmd, SimResourceState resource)
    {
        switch (cmd)
        {
            case LoginCommand login:
                resource.CurrentState = SimResourceState.LoggingIn;
                _eventQueue.Schedule(new LoginCompletedEvent
                {
                    Timestamp  = _clock.Now + resource.AvgLoginTime,
                    ResourceId = resource.Id,
                    UserId     = login.UserId
                });
                break;

            case LogoutCommand:
                resource.CurrentState = SimResourceState.LoggingOut;
                _eventQueue.Schedule(new LogoutCompletedEvent
                {
                    Timestamp  = _clock.Now + resource.AvgLogoutTime,
                    ResourceId = resource.Id
                });
                break;

            case StartProcessCommand start:
                resource.CurrentState  = SimResourceState.SettingUpQueue;
                resource.CurrentQueueId = start.QueueId;

                var queue = _state.Queues[start.QueueId];
                _eventQueue.Schedule(new SetupCompletedEvent
                {
                    Timestamp  = _clock.Now + queue.AvgSetupTime,
                    ResourceId = resource.Id,
                    QueueId    = start.QueueId
                });
                break;

            case StopProcessCommand:
                resource.RequestStopAt = _clock.Now;
                break;
        }
    }
}

/// <summary>
/// Simulator-internal command types produced by IzziStateAdapter.ToSimCommands()
/// and consumed by Worker.ExecuteCommand().
/// These are distinct from Core's ResourceCommand subtypes (Login, Logout, ExecuteQueue).
/// </summary>
public record LoginCommand(Guid UserId);
public record LogoutCommand();
public record StartProcessCommand(Guid QueueId);
public record StopProcessCommand();
