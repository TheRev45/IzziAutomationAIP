namespace IzziAutomationSimulator;

/// <summary>
/// Classe base abstrata para RealSimulator e ForecastSimulator.
/// Contém lógica partilhada de processamento de eventos.
/// 
/// Design pattern: Template Method (subclasses implementam CanAdvance diferente)
/// </summary>
public abstract class SimulatorEngine
{
    protected readonly SimulationClock Clock;
    protected readonly SimulationState State;
    protected readonly EventQueue EventQueue;
    protected readonly SimulatorConfiguration Config;

    protected SimulatorEngine(
        SimulationClock clock,
        SimulationState state,
        EventQueue eventQueue,
        SimulatorConfiguration config)
    {
        Clock = clock;
        State = state;
        EventQueue = eventQueue;
        Config = config;
    }

    /// <summary>
    /// Avança a simulação por um step.
    /// 
    /// Fluxo:
    ///   1. Avança clock
    ///   2. Processa todos os eventos até clock.Now (batch processing)
    ///   3. Delay opcional (para demos em tempo real)
    /// </summary>
    public virtual void Step()
    {
        // 1. Avança relógio
        Clock.Advance(Config.Step);

        // 2. Processa eventos em batch
        ProcessEventsUntil(Clock.Now);

        // 3. Delay para demos (se SpeedMultiplier > 0)
        if (Config.RealDelay > TimeSpan.Zero)
        {
            Thread.Sleep(Config.RealDelay);
        }
    }

    /// <summary>
    /// Processa todos os eventos até um timestamp específico.
    /// Eventos são processados em batches (todos os eventos do mesmo timestamp juntos).
    /// 
    /// CRÍTICO: Batch processing garante que Worker observa estados consistentes.
    /// </summary>
    protected void ProcessEventsUntil(DateTimeOffset until)
    {
        while (EventQueue.NextTimestamp.HasValue && EventQueue.NextTimestamp.Value <= until)
        {
            var batch = EventQueue.GetNextBatch();

            // Processa batch completo atomicamente
            foreach (var evt in batch)
            {
                evt.Apply(State, EventQueue);
            }

            // Após batch completo, Worker pode observar
            // (mas isso é feito externamente, não aqui)
        }
    }

    /// <summary>
    /// Verifica se a simulação pode avançar.
    /// Implementado diferentemente por Real vs Forecast:
    /// 
    /// RealSimulator: pode avançar se há eventos pendentes
    /// ForecastSimulator: pode avançar se queues não vazias E não atingiu horizon
    /// </summary>
    public abstract bool CanAdvance();

    /// <summary>
    /// Exporta dados para Gantt Chart.
    /// Percorre histórico de eventos e constrói timeline de cada recurso.
    /// </summary>
    public virtual List<GanttSegment> ExportGanttData()
    {
        // Placeholder: implementação completa requer tracking de mudanças de estado
        // Por agora, retorna vazio
        return new List<GanttSegment>();
    }
}

/// <summary>
/// Segmento de Gantt Chart (timeline de um recurso).
/// </summary>
public record GanttSegment(
    Guid ResourceId,
    DateTimeOffset Start,
    DateTimeOffset End,
    string State,  // "Working", "Idle", "LoggingIn", etc.
    Guid? QueueId,
    Guid? ItemId,
    bool IsRequestStop  // true se RequestStopAt != null durante este segmento
);
