namespace IzziAutomationSimulator;

/// <summary>
/// Fila de eventos ordenada por timestamp.
/// Armazena todos os eventos futuros (logins, logouts, items completados, etc.) e
/// permite processá-los em batch quando o relógio avança.
/// 
/// Design pattern: Priority Queue (ordenada por tempo)
/// Thread-safety: Não é thread-safe — assume execução single-threaded
/// 
/// Exemplo de uso:
///   eventQueue.Schedule(new LoginCompletedEvent { Timestamp = now + 60s, ... });
///   // ... clock avança ...
///   if (eventQueue.NextTimestamp <= clock.Now)
///   {
///       var batch = eventQueue.GetNextBatch();  // todos os eventos deste timestamp
///       batch.ForEach(evt => evt.Apply(state, eventQueue));
///   }
/// </summary>
public class EventQueue
{
    /// <summary>
    /// Estrutura interna: SortedList ordena automaticamente por chave (timestamp).
    /// Cada timestamp pode ter múltiplos eventos (batch) — por isso o valor é List.
    /// 
    /// Exemplo:
    ///   06:01:00 → [LoginCompleted(R1), LoginCompleted(R2), LoginCompleted(R3)]
    ///   06:01:30 → [SetupCompleted(R1)]
    ///   06:03:25 → [ItemCompleted(R1, A01), ItemCompleted(R2, A02)]
    /// </summary>
    private readonly SortedList<DateTimeOffset, List<SimEvent>> _events = new();

    /// <summary>
    /// Indica se há eventos agendados na fila.
    /// Útil para condição de paragem do simulador.
    /// </summary>
    public bool HasEvents => _events.Count > 0;

    /// <summary>
    /// Timestamp do próximo batch de eventos, ou null se a fila está vazia.
    /// Usado pelo simulador para decidir se deve processar eventos:
    /// 
    ///   while (eventQueue.NextTimestamp <= clock.Now)
    ///   {
    ///       ProcessNextBatch();
    ///   }
    /// </summary>
    public DateTimeOffset? NextTimestamp => _events.Count > 0 ? _events.Keys[0] : null;

    /// <summary>
    /// Número total de eventos agendados (somando todos os batches).
    /// Útil para debugging e logging.
    /// </summary>
    public int TotalEventCount => _events.Values.Sum(batch => batch.Count);

    /// <summary>
    /// Agenda um evento para ser processado no futuro.
    /// Eventos com o mesmo timestamp são agrupados num batch.
    /// 
    /// Importante: O timestamp do evento DEVE ser maior ou igual ao clock.Now
    /// (não faz sentido agendar eventos no passado).
    /// </summary>
    /// <param name="evt">Evento a agendar</param>
    public void Schedule(SimEvent evt)
    {
        if (!_events.ContainsKey(evt.Timestamp))
        {
            _events[evt.Timestamp] = new List<SimEvent>();
        }

        _events[evt.Timestamp].Add(evt);
    }

    /// <summary>
    /// Remove e retorna o próximo batch de eventos (todos os eventos do timestamp mais próximo).
    /// 
    /// Batch processing é crítico para o funcionamento correcto:
    ///   - Se R1 e R2 ficam Idle ao mesmo tempo, ambos os eventos são processados
    ///     ANTES do Worker observar o estado
    ///   - Assim o Worker vê R1=Idle e R2=Idle simultaneamente e chama a Izzi
    ///     UMA vez com o estado correcto de ambos
    /// 
    /// Lança excepção se a fila estiver vazia.
    /// </summary>
    /// <returns>Lista de eventos do próximo timestamp</returns>
    /// <exception cref="InvalidOperationException">Se a fila está vazia</exception>
    public List<SimEvent> GetNextBatch()
    {
        if (_events.Count == 0)
            throw new InvalidOperationException("EventQueue está vazia — não há batch para processar");

        var key = _events.Keys[0];
        var batch = _events[key];
        _events.RemoveAt(0);

        return batch;
    }

    /// <summary>
    /// Remove todos os eventos da fila.
    /// Útil para reiniciar a simulação ou para testes.
    /// </summary>
    public void Clear()
    {
        _events.Clear();
    }

    /// <summary>
    /// Cria uma cópia profunda desta EventQueue.
    /// Usado pelo ForecastSimulator para ter a sua própria fila isolada.
    /// 
    /// Nota: Os eventos são clonados para evitar que modificações no Forecast
    /// afectem a fila do RealSimulator.
    /// </summary>
    /// <returns>Nova EventQueue com cópias de todos os eventos</returns>
    public EventQueue Clone()
    {
        var clone = new EventQueue();

        foreach (var kvp in _events)
        {
            // Cada evento é clonado individualmente
            clone._events[kvp.Key] = kvp.Value.Select(evt => evt.Clone()).ToList();
        }

        return clone;
    }

    /// <summary>
    /// Retorna os próximos N timestamps agendados (para debugging/logging).
    /// </summary>
    /// <param name="count">Número de timestamps a retornar</param>
    /// <returns>Lista de timestamps futuros</returns>
    public List<DateTimeOffset> PeekNext(int count = 5)
    {
        return _events.Keys.Take(count).ToList();
    }

    public override string ToString()
    {
        if (_events.Count == 0)
            return "EventQueue[empty]";

        var nextEvents = _events.Take(3)
            .Select(kvp => $"{kvp.Key:HH:mm:ss}({kvp.Value.Count} events)")
            .ToList();

        var summary = string.Join(", ", nextEvents);
        var remaining = _events.Count > 3 ? $", +{_events.Count - 3} more" : "";

        return $"EventQueue[{summary}{remaining}]";
    }
}
