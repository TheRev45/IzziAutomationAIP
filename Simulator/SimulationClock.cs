namespace IzziAutomationSimulator;

/// <summary>
/// Relógio partilhado entre todos os componentes do simulador.
/// Avança em steps configuráveis e é partilhado por referência entre
/// o RealSimulator, ForecastSimulator e Worker.
/// 
/// Design pattern: Shared State (single source of truth para o tempo simulado)
/// Thread-safety: Não é thread-safe — assume execução single-threaded
/// </summary>
public class SimulationClock
{
    /// <summary>
    /// Timestamp actual da simulação.
    /// Todos os componentes lêem este valor para saber "que horas são" no mundo simulado.
    /// </summary>
    public DateTimeOffset Now { get; private set; }

    /// <summary>
    /// Inicializa o relógio com um timestamp de início.
    /// Tipicamente este é o timestamp do primeiro evento histórico ou o início do dia de trabalho.
    /// </summary>
    /// <param name="startTime">Timestamp inicial da simulação</param>
    public SimulationClock(DateTimeOffset startTime)
    {
        Now = startTime;
    }

    /// <summary>
    /// Avança o relógio por um intervalo de tempo (step).
    /// Chamado pelo loop principal a cada iteração.
    /// 
    /// Exemplo:
    ///   clock.Advance(TimeSpan.FromSeconds(1))  → avança 1 segundo
    ///   clock.Advance(TimeSpan.FromMinutes(5))  → avança 5 minutos
    /// </summary>
    /// <param name="step">Intervalo de tempo a avançar</param>
    public void Advance(TimeSpan step)
    {
        Now += step;
    }

    /// <summary>
    /// Reinicia o relógio para um novo timestamp.
    /// Útil para reiniciar a simulação ou para clonar o relógio no ForecastSimulator.
    /// </summary>
    /// <param name="newTime">Novo timestamp</param>
    public void Reset(DateTimeOffset newTime)
    {
        Now = newTime;
    }

    /// <summary>
    /// Cria uma cópia independente deste relógio.
    /// Usado pelo ForecastSimulator para ter o seu próprio relógio isolado.
    /// </summary>
    /// <returns>Novo relógio com o mesmo timestamp</returns>
    public SimulationClock Clone()
    {
        return new SimulationClock(Now);
    }

    public override string ToString()
    {
        return $"SimulationClock[{Now:yyyy-MM-dd HH:mm:ss}]";
    }
}
