namespace IzziAutomationSimulator;

/// <summary>
/// Worker — coração do sistema de orquestração.
/// 
/// Responsabilidades:
///   1. Observar estado dos recursos
///   2. Detectar triggers para chamar Izzi (timer 10min OU recursos Idle)
///   3. Chamar IzziCore.Run() para obter comandos
///   4. Expandir comandos high-level em sequências temporais
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
    
    private DateTimeOffset _lastIzziCall = DateTimeOffset.MinValue;

    public Worker(
        SimulationState state,
        EventQueue eventQueue,
        SimulationClock clock,
        SimulatorConfiguration config)
    {
        _state = state;
        _eventQueue = eventQueue;
        _clock = clock;
        _config = config;
    }

    /// <summary>
    /// Observa o estado e age se necessário.
    /// 
    /// Triggers para chamar Izzi:
    ///   1. Timer: passaram 10 minutos desde última chamada
    ///   2. Idle: um ou mais recursos ficaram Idle sem comandos pendentes
    /// 
    /// Após chamar Izzi:
    ///   - Expande comandos em sequências temporais
    ///   - Agenda eventos na EventQueue
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

        // Se trigger → chamar Izzi
        if (timerTrigger || idleTrigger)
        {
            CallIzzi();
            _lastIzziCall = _clock.Now;
        }

        // Executar comandos pendentes (se recursos ficaram Idle)
        ExecutePendingCommands();
    }

    /// <summary>
    /// Chama IzziCore para obter comandos de decisão.
    /// 
    /// Fluxo:
    ///   1. Converte estado actual → formato Izzi (UnpopulatedResource[], IzziQueue[])
    ///   2. Chama IzziCore.Run()
    ///   3. Recebe CommandsForResource[]
    ///   4. Expande comandos em sequências temporais
    ///   5. Guarda em PendingCommands de cada recurso
    /// </summary>
    private void CallIzzi()
    {
        // 1. Converter estado para formato Izzi
        var adapter = new IzziStateAdapter(_config);
        var (resources, queues) = adapter.ConvertToIzziFormat(_state);

        // 2. Chamar IzziCore (placeholder: implementação real do algoritmo de decisão)
        var commands = IzziCoreStub.Run(resources, queues);

        // 3. Expandir comandos e atribuir a cada recurso
        foreach (var cmdSet in commands)
        {
            var resource = _state.Resources[cmdSet.ResourceId];
            resource.PendingCommands = ExpandCommands(cmdSet.Commands, resource);
        }
    }

    /// <summary>
    /// Expande comandos high-level em sequências de eventos temporais.
    /// 
    /// Exemplo:
    ///   Input: [Login(Alice), StartProcess(Q_Vendas)]
    ///   Output: [
    ///     LoginCommand(Alice, duration=60s),
    ///     StartProcessCommand(Q_Vendas, setupDuration=30s)
    ///   ]
    /// </summary>
    private List<object> ExpandCommands(List<object> commands, SimResourceState resource)
    {
        var expanded = new List<object>();

        foreach (var cmd in commands)
        {
            // Placeholder: expandir baseado em tipo de comando
            // Na implementação real, cada comando tem duração específica
            expanded.Add(cmd);
        }

        return expanded;
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
            // Só executar se recurso está pronto E tem comandos
            if (resource.PendingCommands.Count == 0)
                continue;

            bool canExecute = resource.CurrentState == SimResourceState.LoggedOut ||
                             resource.CurrentState == SimResourceState.Idle;

            if (!canExecute)
                continue;

            // Pega no próximo comando
            var nextCmd = resource.PendingCommands[0];
            resource.PendingCommands.RemoveAt(0);

            // Executa comando (agenda eventos)
            ExecuteCommand(nextCmd, resource);
        }
    }

    /// <summary>
    /// Executa um comando específico (agenda eventos correspondentes).
    /// 
    /// Comandos possíveis:
    ///   - Login(user) → LoginCompletedEvent após 60s
    ///   - Logout → LogoutCompletedEvent após 30s
    ///   - StartProcess(queue) → SetupCompletedEvent após setup time
    ///   - StopProcess → marca RequestStopAt (paragem passiva)
    /// </summary>
    private void ExecuteCommand(object cmd, SimResourceState resource)
    {
        switch (cmd)
        {
            case LoginCommand login:
                // Recurso começa login
                resource.CurrentState = SimResourceState.LoggingIn;

                // Agenda conclusão
                _eventQueue.Schedule(new LoginCompletedEvent
                {
                    Timestamp = _clock.Now + resource.AvgLoginTime,
                    ResourceId = resource.Id,
                    UserId = login.UserId
                });
                break;

            case LogoutCommand:
                // Recurso começa logout
                resource.CurrentState = SimResourceState.LoggingOut;

                // Agenda conclusão
                _eventQueue.Schedule(new LogoutCompletedEvent
                {
                    Timestamp = _clock.Now + resource.AvgLogoutTime,
                    ResourceId = resource.Id
                });
                break;

            case StartProcessCommand start:
                // Recurso começa setup da queue
                resource.CurrentState = SimResourceState.SettingUpQueue;
                resource.CurrentQueueId = start.QueueId;

                var queue = _state.Queues[start.QueueId];

                // Agenda conclusão do setup
                _eventQueue.Schedule(new SetupCompletedEvent
                {
                    Timestamp = _clock.Now + queue.AvgSetupTime,
                    ResourceId = resource.Id,
                    QueueId = start.QueueId
                });
                break;

            case StopProcessCommand:
                // Paragem passiva: apenas marca timestamp
                // Recurso vai parar quando completar item actual
                resource.RequestStopAt = _clock.Now;
                break;
        }
    }
}

/// <summary>
/// Comandos que a Izzi pode retornar.
/// (Placeholder: na implementação real, estes vêm do IzziCore)
/// </summary>
public record LoginCommand(Guid UserId);
public record LogoutCommand();
public record StartProcessCommand(Guid QueueId);
public record StopProcessCommand();

public record CommandsForResource(
    Guid ResourceId,
    List<object> Commands
);

/// <summary>
/// Stub do IzziCore (motor de decisão).
/// Na implementação real, isto seria o algoritmo de optimização.
/// </summary>
public static class IzziCoreStub
{
    public static List<CommandsForResource> Run(
        List<UnpopulatedResource> resources,
        List<IzziQueue> queues)
    {
        // Placeholder: retorna comandos vazios
        // Implementação real: algoritmo greedy/LP/genetic para optimizar alocação
        return resources.Select(r => new CommandsForResource(r.Id, new List<object>())).ToList();
    }
}

/// <summary>
/// Formato de entrada para IzziCore (vindo do IzziStateAdapter).
/// </summary>
public record UnpopulatedResource(
    Guid Id,
    string State,  // LoggedOut, Idle, Working (mapeado conservadoramente)
    Guid? CurrentUserId,
    TimeSpan LoginTime,
    TimeSpan LogoutTime
);

public record IzziQueue(
    Guid Id,
    Guid UserId,
    int PendingCount,
    TimeSpan SetupTime,
    TimeSpan AvgItemDuration,
    TimeSpan SLA,
    int Criticality
);
