namespace IzziAutomationSimulator;

/// <summary>
/// Estado completo da simulação.
/// Contém todos os recursos, queues e metadata necessária para executar a simulação.
/// 
/// Design: Mutável (alterado por eventos) mas pode ser clonado profundamente para Forecast.
/// </summary>
public class SimulationState
{
    /// <summary>
    /// Todos os recursos da simulação (máquinas RPA, humanos, agentes GenAI).
    /// Indexado por ResourceId para acesso O(1).
    /// </summary>
    public Dictionary<Guid, SimResourceState> Resources { get; init; } = new();

    /// <summary>
    /// Todas as queues de trabalho.
    /// Indexado por QueueId para acesso O(1).
    /// </summary>
    public Dictionary<Guid, SimQueueState> Queues { get; init; } = new();

    /// <summary>
    /// Metadata sobre a simulação (nome, descrição, etc).
    /// </summary>
    public string Name { get; init; } = "Simulation";
    public string? Description { get; init; }

    /// <summary>
    /// Cria uma cópia profunda (deep clone) deste estado.
    /// Usado pelo ForecastSimulator para ter o seu próprio estado isolado.
    /// 
    /// CRÍTICO: Sem deep clone, modificações no Forecast afectariam o Real!
    /// </summary>
    public SimulationState DeepClone()
    {
        var clone = new SimulationState
        {
            Name = Name,
            Description = Description
        };

        // Clona cada recurso
        foreach (var kvp in Resources)
        {
            clone.Resources[kvp.Key] = kvp.Value.DeepClone();
        }

        // Clona cada queue
        foreach (var kvp in Queues)
        {
            clone.Queues[kvp.Key] = kvp.Value.DeepClone();
        }

        return clone;
    }
}

/// <summary>
/// Estado de um recurso (máquina RPA, humano, agente GenAI).
/// </summary>
public class SimResourceState
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    
    /// <summary>
    /// Estado actual do recurso.
    /// Possíveis valores: LoggedOut, LoggingIn, Idle, SettingUpQueue, Working, LoggingOut
    /// </summary>
    public string CurrentState { get; set; } = SimResourceState.LoggedOut;

    /// <summary>
    /// User actualmente logado (null se LoggedOut).
    /// </summary>
    public Guid? CurrentUserId { get; set; }

    /// <summary>
    /// Queue actual (null se não está a trabalhar em nenhuma).
    /// </summary>
    public Guid? CurrentQueueId { get; set; }

    /// <summary>
    /// Item actual sendo processado (null se Idle).
    /// </summary>
    public Guid? CurrentItemId { get; set; }

    /// <summary>
    /// Timestamp quando começou a processar o item actual.
    /// Usado para calcular duração real quando ItemCompletedEvent ocorre.
    /// </summary>
    public DateTimeOffset? LastItemStart { get; set; }

    /// <summary>
    /// Indica se o processo está ligado (recurso pega items automaticamente).
    /// True → recurso trabalha autonomamente
    /// False → recurso para após item actual
    /// </summary>
    public bool ProcessEnabled { get; set; }

    /// <summary>
    /// Timestamp quando foi pedido para parar (RequestStop).
    /// Usado para Gantt (marca segmento com cor diferente).
    /// </summary>
    public DateTimeOffset? RequestStopAt { get; set; }

    /// <summary>
    /// Comandos pendentes que serão executados quando o recurso ficar Idle.
    /// Preenchido pelo Worker após chamar Izzi.
    /// </summary>
    public List<object> PendingCommands { get; set; } = new();

    /// <summary>
    /// Tempos médios de login/logout para este recurso.
    /// Usado para calcular duração de eventos transitórios.
    /// </summary>
    public TimeSpan AvgLoginTime { get; init; } = TimeSpan.FromSeconds(60);
    public TimeSpan AvgLogoutTime { get; init; } = TimeSpan.FromSeconds(30);

    // Estados possíveis (constantes)
    public const string LoggedOut = "LoggedOut";
    public const string LoggingIn = "LoggingIn";
    public const string Idle = "Idle";
    public const string SettingUpQueue = "SettingUpQueue";
    public const string Working = "Working";
    public const string LoggingOut = "LoggingOut";

    public SimResourceState DeepClone()
    {
        return new SimResourceState
        {
            Id = Id,
            Name = Name,
            CurrentState = CurrentState,
            CurrentUserId = CurrentUserId,
            CurrentQueueId = CurrentQueueId,
            CurrentItemId = CurrentItemId,
            LastItemStart = LastItemStart,
            ProcessEnabled = ProcessEnabled,
            RequestStopAt = RequestStopAt,
            PendingCommands = new List<object>(PendingCommands),
            AvgLoginTime = AvgLoginTime,
            AvgLogoutTime = AvgLogoutTime
        };
    }

    public override string ToString()
    {
        var userStr = CurrentUserId.HasValue ? $"User={CurrentUserId}" : "NoUser";
        var queueStr = CurrentQueueId.HasValue ? $"Queue={CurrentQueueId}" : "NoQueue";
        return $"Resource[{Name}, {CurrentState}, {userStr}, {queueStr}]";
    }
}

/// <summary>
/// Estado de uma queue de trabalho.
/// </summary>
public class SimQueueState
{
    public Guid Id { get; init; }
    public string Name { get; init; } = "";
    
    /// <summary>
    /// User associado a esta queue (todas as tasks desta queue são deste user).
    /// </summary>
    public Guid UserId { get; init; }

    /// <summary>
    /// Items ainda não processados (pendentes).
    /// </summary>
    public List<SimTask> PendingItems { get; set; } = new();

    /// <summary>
    /// Items já completados (histórico).
    /// Usado para calcular média de duração de items.
    /// </summary>
    public List<SimFinishedTask> FinishedTasks { get; set; } = new();

    /// <summary>
    /// Tempo médio de setup desta queue (login + configuração ambiente).
    /// </summary>
    public TimeSpan AvgSetupTime { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// SLA desta queue (tempo máximo aceitável por item).
    /// </summary>
    public TimeSpan SLA { get; init; } = TimeSpan.FromHours(2);

    /// <summary>
    /// Criticidade desta queue (1-10, onde 10 é mais crítico).
    /// Usado pela Izzi para priorização.
    /// </summary>
    public int Criticality { get; init; } = 5;

    public SimQueueState DeepClone()
    {
        return new SimQueueState
        {
            Id = Id,
            Name = Name,
            UserId = UserId,
            PendingItems = PendingItems.Select(t => t.Clone()).ToList(),
            FinishedTasks = FinishedTasks.Select(t => t.Clone()).ToList(),
            AvgSetupTime = AvgSetupTime,
            SLA = SLA,
            Criticality = Criticality
        };
    }

    public override string ToString()
    {
        return $"Queue[{Name}, User={UserId}, Pending={PendingItems.Count}, Finished={FinishedTasks.Count}]";
    }
}

/// <summary>
/// Item de trabalho (task) pendente numa queue.
/// </summary>
public class SimTask
{
    public Guid Id { get; init; }
    public Guid QueueId { get; init; }
    
    /// <summary>
    /// Timestamp quando o item foi criado/adicionado à queue.
    /// </summary>
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// Deadline SLA (CreatedAt + Queue.SLA).
    /// </summary>
    public DateTimeOffset SLADeadline { get; init; }

    /// <summary>
    /// Metadata adicional (opcional, para debugging).
    /// </summary>
    public string? Metadata { get; init; }

    public SimTask Clone()
    {
        return new SimTask
        {
            Id = Id,
            QueueId = QueueId,
            CreatedAt = CreatedAt,
            SLADeadline = SLADeadline,
            Metadata = Metadata
        };
    }

    public override string ToString()
    {
        return $"Task[{Id}, Queue={QueueId}, SLA={SLADeadline:HH:mm:ss}]";
    }
}

/// <summary>
/// Item de trabalho já completado (histórico).
/// Usado para calcular médias de duração.
/// </summary>
public class SimFinishedTask
{
    public Guid Id { get; init; }
    public Guid QueueId { get; init; }
    public Guid ResourceId { get; init; }
    
    /// <summary>
    /// Timestamp quando o item foi completado.
    /// </summary>
    public DateTimeOffset CompletedAt { get; init; }

    /// <summary>
    /// Duração real do processamento (em segundos).
    /// </summary>
    public double DurationSeconds { get; init; }

    public SimFinishedTask Clone()
    {
        return new SimFinishedTask
        {
            Id = Id,
            QueueId = QueueId,
            ResourceId = ResourceId,
            CompletedAt = CompletedAt,
            DurationSeconds = DurationSeconds
        };
    }

    public override string ToString()
    {
        return $"Finished[{Id}, Duration={DurationSeconds:F1}s]";
    }
}
