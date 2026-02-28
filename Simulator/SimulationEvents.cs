namespace IzziAutomationSimulator;

/// <summary>
/// Classe base abstrata para todos os eventos do simulador.
/// 
/// Eventos representam acontecimentos que ocorrem em timestamps específicos:
///   - LoginCompleted: recurso terminou login
///   - LogoutCompleted: recurso terminou logout
///   - SetupCompleted: recurso terminou setup e está pronto para trabalhar
///   - ItemCompleted: recurso terminou de processar um item
/// 
/// Design pattern: Command pattern (cada evento sabe como se aplicar ao estado)
/// </summary>
public abstract class SimEvent
{
    /// <summary>
    /// Timestamp em que este evento ocorre.
    /// Usado pela EventQueue para ordenar e agrupar eventos.
    /// </summary>
    public DateTimeOffset Timestamp { get; init; }

    /// <summary>
    /// Aplica o efeito deste evento ao estado da simulação.
    /// Cada tipo de evento implementa a sua própria lógica.
    /// 
    /// Importante: Este método pode agendar NOVOS eventos na EventQueue
    /// (ex: SetupCompleted agenda o primeiro ItemCompleted).
    /// </summary>
    /// <param name="state">Estado actual da simulação (será modificado)</param>
    /// <param name="eventQueue">Fila de eventos (para agendar novos eventos)</param>
    public abstract void Apply(SimulationState state, EventQueue eventQueue);

    /// <summary>
    /// Cria uma cópia profunda deste evento.
    /// Usado quando clonamos a EventQueue para o ForecastSimulator.
    /// </summary>
    public abstract SimEvent Clone();
}

/// <summary>
/// Evento: Login completado.
/// Recurso transitou de LoggingIn → Idle e agora tem um user activo.
/// 
/// Próximo passo típico: Worker executa próximo comando pendente (ex: StartProcess).
/// </summary>
public class LoginCompletedEvent : SimEvent
{
    public Guid ResourceId { get; init; }
    public Guid UserId { get; init; }

    public override void Apply(SimulationState state, EventQueue eventQueue)
    {
        var resource = state.Resources[ResourceId];

        // Recurso fica Idle e com user definido
        resource.CurrentState = SimResourceState.Idle;
        resource.CurrentUserId = UserId;

        // Worker vai detectar Idle no próximo Observe() e executar próximo comando
        // (tipicamente StartProcess que estava em PendingCommands)
    }

    public override SimEvent Clone()
    {
        return new LoginCompletedEvent
        {
            Timestamp = Timestamp,
            ResourceId = ResourceId,
            UserId = UserId
        };
    }

    public override string ToString()
    {
        return $"LoginCompleted[{Timestamp:HH:mm:ss}, Resource={ResourceId}, User={UserId}]";
    }
}

/// <summary>
/// Evento: Logout completado.
/// Recurso transitou de LoggingOut → LoggedOut e perdeu o user activo.
/// 
/// Próximo passo típico: Worker executa Login com novo user (se houver em PendingCommands).
/// </summary>
public class LogoutCompletedEvent : SimEvent
{
    public Guid ResourceId { get; init; }

    public override void Apply(SimulationState state, EventQueue eventQueue)
    {
        var resource = state.Resources[ResourceId];

        // Recurso fica LoggedOut e sem user
        resource.CurrentState = SimResourceState.LoggedOut;
        resource.CurrentUserId = null;

        // Worker vai detectar LoggedOut no próximo Observe() e executar Login
        // (se houver em PendingCommands)
    }

    public override SimEvent Clone()
    {
        return new LogoutCompletedEvent
        {
            Timestamp = Timestamp,
            ResourceId = ResourceId
        };
    }

    public override string ToString()
    {
        return $"LogoutCompleted[{Timestamp:HH:mm:ss}, Resource={ResourceId}]";
    }
}

/// <summary>
/// Evento: Setup completado.
/// Recurso terminou de configurar o ambiente para a queue e está pronto para trabalhar.
/// Transitou de SettingUpQueue → Working e o processo está ligado (ProcessEnabled=true).
/// 
/// Este evento AGENDA AUTOMATICAMENTE o primeiro item — comportamento autónomo do recurso.
/// </summary>
public class SetupCompletedEvent : SimEvent
{
    public Guid ResourceId { get; init; }
    public Guid QueueId { get; init; }

    public override void Apply(SimulationState state, EventQueue eventQueue)
    {
        var resource = state.Resources[ResourceId];
        var queue = state.Queues[QueueId];

        // Recurso fica Working com processo ligado
        resource.CurrentState = SimResourceState.Working;
        resource.CurrentQueueId = QueueId;
        resource.ProcessEnabled = true;

        // Comportamento autónomo: recurso pega no primeiro item imediatamente
        if (queue.PendingItems.Any())
        {
            ScheduleNextItem(resource, queue, state, eventQueue, Timestamp);
        }
        else
        {
            // Queue estava vazia — recurso fica Idle imediatamente
            // (caso raro mas possível se todos os items foram cancelados entretanto)
            resource.CurrentState = SimResourceState.Idle;
            resource.ProcessEnabled = false;
        }
    }

    public override SimEvent Clone()
    {
        return new SetupCompletedEvent
        {
            Timestamp = Timestamp,
            ResourceId = ResourceId,
            QueueId = QueueId
        };
    }

    public override string ToString()
    {
        return $"SetupCompleted[{Timestamp:HH:mm:ss}, Resource={ResourceId}, Queue={QueueId}]";
    }

    /// <summary>
    /// Método auxiliar estático partilhado entre SetupCompletedEvent e ItemCompletedEvent.
    /// Agenda o próximo item da queue para este recurso.
    /// 
    /// Duração do item: média histórica calculada a partir de FinishedTasks.
    /// </summary>
    public static void ScheduleNextItem(
        SimResourceState resource,
        SimQueueState queue,
        SimulationState state,
        EventQueue eventQueue,
        DateTimeOffset now)
    {
        // Skip items already claimed by another resource in the same batch tick.
        // Multiple machines can set up the same queue simultaneously; without this
        // guard they would all pick PendingItems[0] and then crash when Apply()
        // tries to remove an already-removed item.
        var claimed = state.Resources.Values
            .Where(r => r.CurrentItemId.HasValue)
            .Select(r => r.CurrentItemId!.Value)
            .ToHashSet();

        var nextItem = queue.PendingItems.FirstOrDefault(item => !claimed.Contains(item.Id));

        if (nextItem is null)
        {
            // All remaining pending items are currently being processed by other
            // resources — go Idle and let Worker re-trigger Izzi when they finish.
            resource.CurrentState  = SimResourceState.Idle;
            resource.ProcessEnabled = false;
            return;
        }

        // Calcula duração média do item baseada no histórico
        var avgDuration = queue.FinishedTasks.Any()
            ? TimeSpan.FromSeconds(queue.FinishedTasks.Average(t => t.DurationSeconds))
            : TimeSpan.FromMinutes(3); // fallback se sem histórico

        // Agenda o evento de conclusão do item
        eventQueue.Schedule(new ItemCompletedEvent
        {
            Timestamp = now + avgDuration,
            ResourceId = resource.Id,
            ItemId = nextItem.Id,
            QueueId = queue.Id
        });

        // Actualiza estado do recurso
        resource.CurrentItemId = nextItem.Id;
        resource.LastItemStart = now;
    }
}

/// <summary>
/// Evento: Item completado.
/// Recurso terminou de processar um item da queue.
/// 
/// Comportamento autónomo crítico:
///   - Se ProcessEnabled=true E queue tem mais items → agenda próximo item automaticamente
///   - Se queue vazia OU ProcessEnabled=false → recurso fica Idle (trigger para Izzi)
/// </summary>
public class ItemCompletedEvent : SimEvent
{
    public Guid ResourceId { get; init; }
    public Guid ItemId { get; init; }
    public Guid QueueId { get; init; }

    public override void Apply(SimulationState state, EventQueue eventQueue)
    {
        var resource = state.Resources[ResourceId];
        var queue = state.Queues[QueueId];

        // Calcula duração real do item
        var duration = Timestamp - resource.LastItemStart!.Value;

        // Remove o item completado da queue
        var completedItem = queue.PendingItems.First(item => item.Id == ItemId);
        queue.PendingItems.Remove(completedItem);

        // Move item para FinishedTasks (para cálculo de médias)
        queue.FinishedTasks.Add(new SimFinishedTask
        {
            Id = ItemId,
            QueueId = QueueId,
            ResourceId = ResourceId,
            CompletedAt = Timestamp,
            DurationSeconds = duration.TotalSeconds
        });

        // Limpa referência ao item actual
        resource.CurrentItemId = null;
        resource.LastItemStart = null;

        // Comportamento autónomo: se processo está ligado E há mais items → continuar
        if (resource.ProcessEnabled && queue.PendingItems.Any())
        {
            // Pega no próximo item automaticamente (SEM chamar Izzi)
            SetupCompletedEvent.ScheduleNextItem(resource, queue, state, eventQueue, Timestamp);
        }
        else
        {
            // Queue vazia OU processo foi parado (RequestStop) → Idle
            resource.CurrentState = SimResourceState.Idle;
            resource.ProcessEnabled = false;

            // Worker vai detectar Idle sem PendingCommands → trigger Izzi
        }
    }

    public override SimEvent Clone()
    {
        return new ItemCompletedEvent
        {
            Timestamp = Timestamp,
            ResourceId = ResourceId,
            ItemId = ItemId,
            QueueId = QueueId
        };
    }

    public override string ToString()
    {
        return $"ItemCompleted[{Timestamp:HH:mm:ss}, Resource={ResourceId}, Item={ItemId}, Queue={QueueId}]";
    }
}
