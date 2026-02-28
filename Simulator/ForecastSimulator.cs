namespace IzziAutomationSimulator;

/// <summary>
/// Simulador de Forecast — projecta futuro de forma assíncrona.
/// 
/// Usado para:
///   - Prever quando queues ficam vazias
///   - Detectar SLA violations futuras
///   - Gerar acções prescritivas (alocar recursos antes do problema)
/// 
/// Condição de paragem: queues vazias OU atingiu ForecastHorizon (ex: 8h).
/// </summary>
public class ForecastSimulator : SimulatorEngine
{
    private readonly Worker _worker;
    private readonly DateTimeOffset _forecastStartTime;

    public ForecastSimulator(
        SimulationClock clock,
        SimulationState state,
        EventQueue eventQueue,
        SimulatorConfiguration config)
        : base(clock, state, eventQueue, config)
    {
        _worker = new Worker(state, eventQueue, clock, config);
        _forecastStartTime = clock.Now;
    }

    /// <summary>
    /// Cria ForecastSimulator a partir do RealSimulator (deep clone).
    /// 
    /// CRÍTICO: Clone profundo garante que Forecast não afecta Real!
    /// </summary>
    public static ForecastSimulator FromReal(RealSimulator realSim, SimulatorConfiguration config)
    {
        // Clone estado, clock e eventQueue
        var clonedState = realSim.State.DeepClone();
        var clonedClock = realSim.Clock.Clone();
        var clonedEventQueue = realSim.EventQueue.Clone();

        return new ForecastSimulator(clonedClock, clonedState, clonedEventQueue, config);
    }

    /// <summary>
    /// Forecast pode avançar se:
    ///   - Há queues com items pendentes E
    ///   - Não ultrapassou ForecastHorizon
    /// </summary>
    public override bool CanAdvance()
    {
        // Verifica se alguma queue tem items
        bool hasWork = State.Queues.Values.Any(q => q.PendingItems.Any());
        if (!hasWork)
            return false;

        // Verifica se atingiu horizon
        var elapsed = Clock.Now - _forecastStartTime;
        if (elapsed >= Config.ForecastHorizon)
            return false;

        return true;
    }

    /// <summary>
    /// Executa forecast completo até condição de paragem.
    /// 
    /// IMPORTANTE: Deve correr em Task.Run separado para não bloquear Real!
    /// </summary>
    public ForecastResult RunForecast(CancellationToken cancellationToken = default)
    {
        while (CanAdvance() && !cancellationToken.IsCancellationRequested)
        {
            Step();
            _worker.Observe();
        }

        return GenerateForecastResult();
    }

    /// <summary>
    /// Versão assíncrona (recomendada).
    /// </summary>
    public async Task<ForecastResult> RunForecastAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() => RunForecast(cancellationToken), cancellationToken);
    }

    /// <summary>
    /// Gera resultado do forecast baseado no estado final.
    /// 
    /// Retorna:
    ///   - ETAs de conclusão de cada queue
    ///   - Alertas (SLA violations previstas)
    ///   - Acções prescritivas (alocar recursos, etc)
    /// </summary>
    private ForecastResult GenerateForecastResult()
    {
        var result = new ForecastResult
        {
            GeneratedAt = _forecastStartTime,
            HorizonEnd = Clock.Now,
            QueueCompletionETAs = new Dictionary<Guid, DateTimeOffset>(),
            Alerts = new List<ForecastAlert>(),
            RecommendedActions = new List<PrescriptiveAction>()
        };

        // Calcula ETA de cada queue
        foreach (var queue in State.Queues.Values)
        {
            if (queue.PendingItems.Count == 0)
            {
                // Queue completou dentro do horizon
                result.QueueCompletionETAs[queue.Id] = Clock.Now;
            }
            else
            {
                // Queue ainda tem items → não completou no horizon
                // ETA estimado baseado em throughput médio
                var avgItemDuration = queue.FinishedTasks.Any()
                    ? TimeSpan.FromSeconds(queue.FinishedTasks.Average(t => t.DurationSeconds))
                    : TimeSpan.FromMinutes(3);

                var resourcesWorking = State.Resources.Values
                    .Count(r => r.CurrentQueueId == queue.Id && r.ProcessEnabled);

                if (resourcesWorking > 0)
                {
                    var remainingTime = TimeSpan.FromSeconds(
                        queue.PendingItems.Count * avgItemDuration.TotalSeconds / resourcesWorking
                    );
                    result.QueueCompletionETAs[queue.Id] = Clock.Now + remainingTime;
                }
                else
                {
                    // Sem recursos alocados → ETA unknown
                    result.QueueCompletionETAs[queue.Id] = DateTimeOffset.MaxValue;
                }
            }
        }

        // Detecta SLA violations
        foreach (var queue in State.Queues.Values)
        {
            var violatedItems = queue.PendingItems
                .Where(item => item.SLADeadline < result.QueueCompletionETAs.GetValueOrDefault(queue.Id, DateTimeOffset.MaxValue))
                .ToList();

            if (violatedItems.Any())
            {
                result.Alerts.Add(new ForecastAlert
                {
                    Severity = "critical",
                    QueueId = queue.Id,
                    Description = $"Queue {queue.Name}: {violatedItems.Count} items vão violar SLA",
                    PredictedTime = violatedItems.Min(i => i.SLADeadline),
                    LeadTime = violatedItems.Min(i => i.SLADeadline) - _forecastStartTime
                });

                // Gera acção prescritiva
                result.RecommendedActions.Add(new PrescriptiveAction
                {
                    ActionType = "ALLOCATE_RESOURCE",
                    TargetQueueId = queue.Id,
                    ResourceDelta = Math.Max(1, violatedItems.Count / 10), // rough estimate
                    Rationale = $"Prevenir {violatedItems.Count} SLA violations",
                    ConfidenceLevel = 0.75
                });
            }
        }

        return result;
    }
}

/// <summary>
/// Resultado do forecast.
/// </summary>
public class ForecastResult
{
    public DateTimeOffset GeneratedAt { get; init; }
    public DateTimeOffset HorizonEnd { get; init; }
    public Dictionary<Guid, DateTimeOffset> QueueCompletionETAs { get; init; } = new();
    public List<ForecastAlert> Alerts { get; init; } = new();
    public List<PrescriptiveAction> RecommendedActions { get; init; } = new();
}

/// <summary>
/// Alerta gerado pelo forecast.
/// </summary>
public class ForecastAlert
{
    public string Severity { get; init; } = "warning"; // warning, critical
    public Guid? QueueId { get; init; }
    public string Description { get; init; } = "";
    public DateTimeOffset PredictedTime { get; init; }
    public TimeSpan LeadTime { get; init; }
}

/// <summary>
/// Acção prescritiva sugerida pelo forecast.
/// </summary>
public class PrescriptiveAction
{
    public string ActionType { get; init; } = ""; // ALLOCATE_RESOURCE, REALLOCATE, etc
    public Guid? TargetQueueId { get; init; }
    public int ResourceDelta { get; init; } // +2 recursos, -1 recurso, etc
    public string Rationale { get; init; } = "";
    public double ConfidenceLevel { get; init; } // 0.0 to 1.0
}
