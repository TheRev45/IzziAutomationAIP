namespace IzziAutomationSimulator;

/// <summary>
/// Simulador Real — processa eventos históricos + tempo real.
/// 
/// Usado para:
///   - Replay de eventos históricos (testar cenários passados)
///   - Simulação em tempo real (produção)
/// 
/// Condição de paragem: sem eventos pendentes na EventQueue.
/// </summary>
public class RealSimulator : SimulatorEngine
{
    private readonly Worker _worker;

    public RealSimulator(
        SimulationClock clock,
        SimulationState state,
        EventQueue eventQueue,
        SimulatorConfiguration config)
        : base(clock, state, eventQueue, config)
    {
        // Worker observa estado e chama Izzi quando necessário
        _worker = new Worker(state, eventQueue, clock, config);
    }

    /// <summary>
    /// Real pode avançar enquanto houver eventos agendados.
    /// Quando EventQueue vazia → simulação terminou.
    /// </summary>
    public override bool CanAdvance()
    {
        return EventQueue.HasEvents;
    }

    /// <summary>
    /// Loop principal do RealSimulator.
    /// 
    /// Fluxo:
    ///   1. Avança clock + processa eventos
    ///   2. Worker observa estado
    ///   3. Worker chama Izzi se trigger (10min OU Idle)
    ///   4. Worker executa comandos pendentes
    ///   5. Repete até EventQueue vazia
    /// </summary>
    public void Run()
    {
        while (CanAdvance())
        {
            // 1. Avança simulação
            Step();

            // 2. Worker observa + age
            _worker.Observe();
        }
    }

    /// <summary>
    /// Versão assíncrona do Run (para não bloquear UI).
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        while (CanAdvance() && !cancellationToken.IsCancellationRequested)
        {
            Step();
            _worker.Observe();

            // Yield para não bloquear thread
            if (Config.SpeedMultiplier == 0)
                await Task.Yield();
        }
    }

    /// <summary>
    /// Obtém forecast mais recente gerado pelo ForecastSimulator.
    /// (Real não gera forecast, mas pode ter referência ao último gerado)
    /// </summary>
    public ForecastResult? GetLatestForecast()
    {
        // Placeholder: em implementação completa, ForecastSimulator actualiza isto
        return null;
    }
}
