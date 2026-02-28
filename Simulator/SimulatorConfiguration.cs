namespace IzziAutomationSimulator;

/// <summary>
/// Configuração global do simulador.
/// Controla a granularidade temporal, velocidade de demo e parâmetros da Izzi.
/// 
/// Imutável após construção (init-only properties) para evitar modificações acidentais durante execução.
/// Record permite sintaxe 'with' para criar cópias modificadas: config with { Step = TimeSpan.FromSeconds(5) }
/// </summary>
public record SimulatorConfiguration
{
    /// <summary>
    /// Granularidade do relógio simulado.
    /// Define de quanto em quanto tempo o clock avança em cada iteração do loop.
    /// 
    /// Exemplos:
    ///   TimeSpan.FromSeconds(1)  → precisão ao segundo (recomendado)
    ///   TimeSpan.FromSeconds(5)  → precisão a cada 5 segundos (mais rápido)
    ///   TimeSpan.FromMinutes(1)  → precisão ao minuto (muito rápido mas menos preciso)
    /// 
    /// Impacto:
    ///   - Valores menores → simulação mais precisa mas mais lenta
    ///   - Valores maiores → simulação mais rápida mas pode perder eventos entre steps
    /// </summary>
    public TimeSpan Step { get; init; } = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Intervalo máximo entre chamadas à Izzi.
    /// Mesmo que não haja triggers (recursos Idle), a Izzi é chamada a cada N minutos
    /// para reavaliar a distribuição de trabalho.
    /// 
    /// Trigger independente: se um recurso fica Idle, a Izzi é chamada imediatamente,
    /// ignorando este timer.
    /// 
    /// Valor típico: 10 minutos
    /// </summary>
    public TimeSpan IzziTimerInterval { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Horizonte de decisão da Izzi (discretization time).
    /// Define a "janela temporal" que a Izzi considera ao calcular RealCapacity:
    /// 
    ///   RealCapacity = floor((DiscTime - SetupOverhead) / AvgItemDuration)
    /// 
    /// Exemplo:
    ///   DiscTime = 10 minutos
    ///   Setup = 2 minutos
    ///   AvgItem = 3 minutos
    ///   → RealCapacity = floor((10-2)/3) = 2 items
    /// 
    /// Valor típico: 10 minutos (igual ao timer para consistência)
    /// </summary>
    public TimeSpan IzziDiscTime { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Limite máximo de tempo que o ForecastSimulator pode simular.
    /// Previne loops infinitos e limita o custo computacional do forecast.
    /// 
    /// O Forecast para quando:
    ///   - Todas as queues ficam vazias, OU
    ///   - Este horizonte é atingido
    /// 
    /// Valor típico: 8 horas (jornada de trabalho)
    /// </summary>
    public TimeSpan ForecastHorizon { get; init; } = TimeSpan.FromHours(8);

    /// <summary>
    /// Multiplicador de velocidade para demos em tempo real.
    /// Controla quanto tempo real passa por cada step simulado.
    /// 
    /// Cálculo: RealDelay = Step / SpeedMultiplier
    /// 
    /// Exemplos:
    ///   1.0  → tempo real (1 segundo simulado = 1 segundo real)
    ///   10.0 → 10× mais rápido (1 segundo simulado = 0.1s real)
    ///   60.0 → 1 hora simulada em 1 minuto real
    ///   0.0  → sem delay (máxima velocidade, útil para testes)
    /// 
    /// Para testes automatizados, usar 0.0
    /// Para demos visuais, usar 10.0 a 60.0
    /// </summary>
    public double SpeedMultiplier { get; init; } = 1.0;

    /// <summary>
    /// Delay real entre steps do simulador.
    /// Calculado automaticamente a partir de Step e SpeedMultiplier.
    /// 
    /// Se SpeedMultiplier = 0, retorna TimeSpan.Zero (sem delay).
    /// </summary>
    public TimeSpan RealDelay
    {
        get
        {
            if (SpeedMultiplier == 0)
                return TimeSpan.Zero;

            return TimeSpan.FromMilliseconds(Step.TotalMilliseconds / SpeedMultiplier);
        }
    }

    /// <summary>
    /// Valida a configuração e lança excepções se valores inválidos forem detectados.
    /// Chamado tipicamente após construção para fail-fast.
    /// </summary>
    public void Validate()
    {
        if (Step <= TimeSpan.Zero)
            throw new ArgumentException("Step deve ser maior que zero", nameof(Step));

        if (IzziTimerInterval <= TimeSpan.Zero)
            throw new ArgumentException("IzziTimerInterval deve ser maior que zero", nameof(IzziTimerInterval));

        if (IzziDiscTime <= TimeSpan.Zero)
            throw new ArgumentException("IzziDiscTime deve ser maior que zero", nameof(IzziDiscTime));

        if (ForecastHorizon <= TimeSpan.Zero)
            throw new ArgumentException("ForecastHorizon deve ser maior que zero", nameof(ForecastHorizon));

        if (SpeedMultiplier < 0)
            throw new ArgumentException("SpeedMultiplier não pode ser negativo", nameof(SpeedMultiplier));
    }

    /// <summary>
    /// Configuração padrão recomendada para produção.
    /// </summary>
    public static SimulatorConfiguration Default => new()
    {
        Step = TimeSpan.FromSeconds(1),
        IzziTimerInterval = TimeSpan.FromMinutes(10),
        IzziDiscTime = TimeSpan.FromMinutes(10),
        ForecastHorizon = TimeSpan.FromHours(8),
        SpeedMultiplier = 1.0
    };

    /// <summary>
    /// Configuração para testes automatizados (máxima velocidade, sem delays).
    /// </summary>
    public static SimulatorConfiguration FastTest => new()
    {
        Step = TimeSpan.FromSeconds(1),
        IzziTimerInterval = TimeSpan.FromMinutes(10),
        IzziDiscTime = TimeSpan.FromMinutes(10),
        ForecastHorizon = TimeSpan.FromHours(8),
        SpeedMultiplier = 0.0  // sem delay
    };

    /// <summary>
    /// Configuração para demos visuais (60× mais rápido que tempo real).
    /// 1 hora simulada passa em 1 minuto real.
    /// </summary>
    public static SimulatorConfiguration Demo => new()
    {
        Step = TimeSpan.FromSeconds(1),
        IzziTimerInterval = TimeSpan.FromMinutes(10),
        IzziDiscTime = TimeSpan.FromMinutes(10),
        ForecastHorizon = TimeSpan.FromHours(8),
        SpeedMultiplier = 60.0
    };

    public override string ToString()
    {
        return $"SimulatorConfig[Step={Step.TotalSeconds}s, IzziTimer={IzziTimerInterval.TotalMinutes}min, " +
               $"DiscTime={IzziDiscTime.TotalMinutes}min, ForecastHorizon={ForecastHorizon.TotalHours}h, " +
               $"Speed={SpeedMultiplier}×]";
    }
}
