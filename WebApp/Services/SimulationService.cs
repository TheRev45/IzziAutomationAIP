using IzziAutomationCore;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationSimulator;
using IzziWebApp.Connectors.BluePrism;
using IzziWebApp.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IzziWebApp.Services;

// ═══════════════════════════════════════════════════════════
// DTOs (camelCased by System.Text.Json for the browser)
// ═══════════════════════════════════════════════════════════

public record SimStateDto(
    string   Clock,
    long     ClockMs,
    long     StartMs,
    long     EndMs,
    bool     IsRunning,
    bool     IsFinished,
    double   Speed,
    List<ResourceDto>           Resources,
    List<QueueDto>              Queues,
    List<GanttBlockDto>         GanttBlocks,
    List<GanttBlockDto>         ForecastGanttBlocks,
    List<string>                Events,
    MetricsDto                  Metrics,
    Dictionary<string, string>  QueueColors   // queueName → hex color
);

public record ResourceDto(string Name, string State, string? Queue, string? User, string? QueueColor);
public record QueueDto(string Name, int Pending, int Completed, string Color);
public record MetricsDto(Dictionary<string, double> TasksPerHour, Dictionary<string, double> Utilization);

public class GanttBlockDto
{
    public string  Resource  { get; init; } = "";
    public string  BlockType { get; init; } = "";
    public string? Queue     { get; init; }
    public string  Color     { get; init; } = "";
    public long    StartMs   { get; init; }
    public long?   EndMs     { get; init; }
}

// ═══════════════════════════════════════════════════════════
// SimulationService
// ═══════════════════════════════════════════════════════════

public class SimulationService
{
    private readonly IHubContext<SimulationHub> _hub;
    private readonly string                    _dataPath;

    // ── Colour palette (up to 10 queues) ────────────────────
    private static readonly string[] ColorPalette =
    {
        "#e74c3c", "#3498db", "#2ecc71", "#f39c12", "#9b59b6",
        "#1abc9c", "#e67e22", "#2980b9", "#27ae60", "#8e44ad",
    };

    // ── Load result (populated in InitSimulation) ────────────
    private SimulationLoadResult _load = null!;

    // ── Live simulation state ────────────────────────────────
    private SimulationState         _state      = null!;
    private EventQueue              _eventQueue = null!;
    private SimulationClock         _clock      = null!;
    private Worker                  _worker     = null!;
    private SimulatorConfiguration  _config     = SimulatorConfiguration.FastTest;

    // ── Control ──────────────────────────────────────────────
    private CancellationTokenSource _cts      = new();
    private Task?                   _runTask;
    private int                     _nextWaveIndex;
    private bool                    _isRunning;
    private bool                    _isFinished;
    private double                  _speed = 60.0;

    // ── Gantt tracking ───────────────────────────────────────
    private readonly List<GanttBlockDto>         _completedBlocks = new();
    private readonly Dictionary<Guid, OpenBlock> _openBlocks      = new();

    // ── Event log ────────────────────────────────────────────
    private readonly Queue<string> _eventLog    = new();
    private const int MaxLogEntries              = 100;

    // ── Metrics ──────────────────────────────────────────────
    private readonly Dictionary<Guid, double> _utilSeconds    = new();
    private readonly Dictionary<Guid, int>    _tasksCompleted = new();

    // ── Instance-level name + colour maps ───────────────────
    private Dictionary<Guid, string> _names       = new();
    private Dictionary<Guid, string> _queueColors = new();

    // ── Broadcast rate-limiting ──────────────────────────────
    private DateTime _lastBroadcast = DateTime.MinValue;

    // ── Izzi call counter ────────────────────────────────────
    private int _izziCallCount;

    // ── Forecast Gantt ────────────────────────────────────────
    private volatile List<GanttBlockDto> _forecastBlocks = new();
    private CancellationTokenSource      _forecastCts    = new();

    // ── Internal open-block ─────────────────────────────────
    private class OpenBlock
    {
        public string  Resource  { get; set; } = "";
        public string  BlockType { get; set; } = "";
        public string? Queue     { get; set; }
        public string  Color     { get; set; } = "";
        public long    StartMs   { get; set; }
    }

    // ════════════════════════════════════════════════════════
    // Constructor
    // ════════════════════════════════════════════════════════

    public SimulationService(IHubContext<SimulationHub> hub, string dataPath)
    {
        _hub      = hub;
        _dataPath = dataPath;
        InitSimulation();
    }

    // ════════════════════════════════════════════════════════
    // Initialisation (called on first load and on Reset)
    // ════════════════════════════════════════════════════════

    private void InitSimulation()
    {
        // Load from Blue Prism CSVs
        _load = BluePrismConnector.Load(_dataPath);

        // Deep-clone the initial state (so Reset is always a fresh start)
        _state      = _load.InitialState.DeepClone();
        _eventQueue = new EventQueue();
        _clock      = new SimulationClock(_load.SimStart);
        _names      = new Dictionary<Guid, string>(_load.Names);

        _nextWaveIndex = 0;
        _isRunning  = false;
        _isFinished = false;
        _completedBlocks.Clear();
        _openBlocks.Clear();
        _eventLog.Clear();
        _utilSeconds.Clear();
        _tasksCompleted.Clear();
        _izziCallCount = 0;

        _forecastBlocks = new List<GanttBlockDto>();
        _forecastCts.Cancel();
        _forecastCts = new CancellationTokenSource();

        // Assign queue colours in declaration order
        int ci = 0;
        _queueColors = _state.Queues.Values
            .ToDictionary(q => q.Id, _ => ColorPalette[ci++ % ColorPalette.Length]);

        // Initialise per-resource metrics
        foreach (var id in _state.Resources.Keys)
            _utilSeconds[id] = 0;

        // Initialise per-queue task counters
        foreach (var id in _state.Queues.Keys)
            _tasksCompleted[id] = 0;

        _config = SimulatorConfiguration.FastTest;
        var rawCore     = IzziCoreBuilder.Build(_config.IzziDiscTime);
        var loggingCore = new WebLoggingCore(rawCore, this);
        _worker = new Worker(_state, _eventQueue, _clock, _config, loggingCore);

        AddEvent($"Loaded {_state.Resources.Count} bots, " +
                 $"{_state.Queues.Count} queues, " +
                 $"{_load.TaskWaves.Sum(w => w.Tasks.Count)} tasks " +
                 $"in {_load.TaskWaves.Count} waves");
        AddEvent($"Sim window: {_load.SimStart:HH:mm} → {_load.SimEnd:HH:mm}");
    }

    // ════════════════════════════════════════════════════════
    // Control API (called from SimulationHub)
    // ════════════════════════════════════════════════════════

    public async Task StartAsync()
    {
        if (_isRunning || _isFinished) return;

        _isRunning = true;
        _cts       = new CancellationTokenSource();
        _runTask   = Task.Run(() => RunTickLoopAsync(_cts.Token));

        AddEvent("▶ Simulation started");
        await BroadcastStateForce();
    }

    public async Task PauseAsync()
    {
        if (!_isRunning) return;

        _isRunning = false;
        _cts.Cancel();

        AddEvent("⏸ Simulation paused");
        await BroadcastStateForce();
    }

    public async Task ResumeAsync()
    {
        if (_isRunning || _isFinished) return;

        _isRunning = true;
        _cts       = new CancellationTokenSource();
        _runTask   = Task.Run(() => RunTickLoopAsync(_cts.Token));

        AddEvent("▶ Simulation resumed");
        await BroadcastStateForce();
    }

    public async Task ResetAsync()
    {
        _isRunning = false;
        _cts.Cancel();

        if (_runTask is not null)
        {
            try   { await _runTask; }
            catch { /* cancelled or finished */ }
        }

        InitSimulation();
        AddEvent("↺ Simulation reset");
        await BroadcastStateForce();
    }

    public void SetSpeed(double multiplier) =>
        _speed = Math.Max(0.1, multiplier);

    // ════════════════════════════════════════════════════════
    // Tick loop
    // ════════════════════════════════════════════════════════

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _clock.Now < _load.SimEnd)
            {
                var tickStart = DateTime.UtcNow;

                try
                {
                    ProcessTick();
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _isRunning  = false;
                    _isFinished = true;
                    AddEvent($"❌ TICK EXCEPTION: {ex.GetType().Name}: {ex.Message}");
                    if (ex.InnerException is not null)
                        AddEvent($"   inner: {ex.InnerException.GetType().Name}: {ex.InnerException.Message}");
                    AddEvent($"   at: {ex.StackTrace?.Split('\n').FirstOrDefault()?.Trim() ?? "unknown"}");
                    await BroadcastStateForce();
                    return;
                }

                // All waves injected + queues drained + no pending events → done
                if (_nextWaveIndex >= _load.TaskWaves.Count
                    && _state.Queues.Values.All(q => q.PendingItems.Count == 0)
                    && !_eventQueue.HasEvents)
                {
                    _isFinished = true;
                    _isRunning  = false;
                    AddEvent("✓ Simulation complete — all tasks done!");
                    await BroadcastStateForce();
                    return;
                }

                await BroadcastStateTick();

                if (_speed > 0)
                {
                    var delayMs = 1000.0 / _speed;
                    var elapsed = (DateTime.UtcNow - tickStart).TotalMilliseconds;
                    var wait    = delayMs - elapsed;
                    if (wait > 1)
                        await Task.Delay((int)wait, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* normal pause/reset */ }
        finally
        {
            _isRunning = false;

            if (_clock.Now >= _load.SimEnd && !_isFinished)
            {
                _isFinished = true;
                AddEvent($"Simulation window ended at {_clock.Now:HH:mm}");
                await BroadcastStateForce();
            }
        }
    }

    // ════════════════════════════════════════════════════════
    // Per-tick logic
    // ════════════════════════════════════════════════════════

    private void ProcessTick()
    {
        _clock.Advance(_config.Step);   // +1 second

        // ── Inject all task waves due at or before clock.Now ────
        while (_nextWaveIndex < _load.TaskWaves.Count
               && _clock.Now >= _load.TaskWaves[_nextWaveIndex].At)
        {
            var wave = _load.TaskWaves[_nextWaveIndex++];
            foreach (var task in wave.Tasks)
                _state.Queues[task.QueueId].PendingItems.Add(task);

            var queueSummary = wave.Tasks
                .GroupBy(t => GetName(t.QueueId))
                .Select(g => $"{g.Key}+{g.Count()}")
                .Aggregate((a, b) => a + " " + b);
            AddEvent($"→ {wave.Tasks.Count} task(s) loaded [{queueSummary}]");
        }

        // ── Event processing ─────────────────────────────────
        var snapBefore = SnapshotStates();

        while (_eventQueue.NextTimestamp.HasValue
               && _eventQueue.NextTimestamp.Value <= _clock.Now)
        {
            foreach (var evt in _eventQueue.GetNextBatch())
            {
                if (evt is ItemCompletedEvent ice)
                    RecordItemCompleted(ice);
                evt.Apply(_state, _eventQueue);
            }
        }

        DetectTransitions(snapBefore);

        // ── Worker.Observe() ─────────────────────────────────
        var snapBeforeWorker = SnapshotStates();
        _worker.Observe();
        DetectTransitions(snapBeforeWorker);

        UpdateUtilization();
    }

    private void RecordItemCompleted(ItemCompletedEvent ice)
    {
        if (_tasksCompleted.ContainsKey(ice.QueueId))
            _tasksCompleted[ice.QueueId]++;

        AddEvent($"✓ {GetName(ice.ResourceId)} completed {GetName(ice.QueueId)} item");
    }

    // ════════════════════════════════════════════════════════
    // Gantt block tracking
    // ════════════════════════════════════════════════════════

    private Dictionary<Guid, (string State, Guid? QueueId)> SnapshotStates() =>
        _state.Resources.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.CurrentState, kv.Value.CurrentQueueId));

    private void DetectTransitions(Dictionary<Guid, (string State, Guid? QueueId)> before)
    {
        foreach (var (id, res) in _state.Resources)
        {
            var prev = before[id];
            if (res.CurrentState == prev.State && res.CurrentQueueId == prev.QueueId)
                continue;

            var resName = GetName(id);
            var prevQ   = prev.QueueId.HasValue   ? $"({GetName(prev.QueueId.Value)})"  : "";
            var currQ   = res.CurrentQueueId.HasValue ? $"({GetName(res.CurrentQueueId.Value)})" : "";
            AddEvent($"  {resName}: {prev.State}{prevQ} → {res.CurrentState}{currQ}");

            // Close current open block
            if (_openBlocks.TryGetValue(id, out var ob))
            {
                _completedBlocks.Add(new GanttBlockDto
                {
                    Resource  = ob.Resource,
                    BlockType = ob.BlockType,
                    Queue     = ob.Queue,
                    Color     = ob.Color,
                    StartMs   = ob.StartMs,
                    EndMs     = _clock.Now.ToUnixTimeMilliseconds(),
                });
                _openBlocks.Remove(id);
            }

            // Open new block
            var blockType = GetBlockType(res.CurrentState);
            if (blockType is not null)
            {
                _openBlocks[id] = new OpenBlock
                {
                    Resource  = resName,
                    BlockType = blockType,
                    Queue     = res.CurrentQueueId.HasValue ? GetName(res.CurrentQueueId.Value) : null,
                    Color     = GetBlockColor(res.CurrentState, res.CurrentQueueId),
                    StartMs   = _clock.Now.ToUnixTimeMilliseconds(),
                };
            }
        }
    }

    private static string? GetBlockType(string state) => state switch
    {
        SimResourceState.LoggingIn      => "login",
        SimResourceState.LoggingOut     => "logout",
        SimResourceState.SettingUpQueue => "setup",
        SimResourceState.Working        => "working",
        _                               => null
    };

    private string GetBlockColor(string state, Guid? queueId)
    {
        return state switch
        {
            SimResourceState.LoggingIn  or SimResourceState.LoggingOut => "#7f8c8d",
            SimResourceState.SettingUpQueue
                when queueId.HasValue && _queueColors.TryGetValue(queueId.Value, out var c) => LightenHex(c),
            SimResourceState.Working
                when queueId.HasValue && _queueColors.TryGetValue(queueId.Value, out var c) => c,
            _ => "#555"
        };
    }

    private static string LightenHex(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#')
        {
            int r = (Convert.ToInt32(hex[1..3], 16) + 220) / 2;
            int g = (Convert.ToInt32(hex[3..5], 16) + 220) / 2;
            int b = (Convert.ToInt32(hex[5..7], 16) + 220) / 2;
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        return hex;
    }

    // ════════════════════════════════════════════════════════
    // Metrics
    // ════════════════════════════════════════════════════════

    private void UpdateUtilization()
    {
        foreach (var (id, res) in _state.Resources)
        {
            if (res.CurrentState == SimResourceState.Working)
                _utilSeconds[id] = _utilSeconds.GetValueOrDefault(id) + _config.Step.TotalSeconds;
        }
    }

    // ════════════════════════════════════════════════════════
    // Event log
    // ════════════════════════════════════════════════════════

    private void AddEvent(string msg)
    {
        _eventLog.Enqueue($"[{_clock.Now:HH:mm:ss}] {msg}");
        while (_eventLog.Count > MaxLogEntries)
            _eventLog.Dequeue();
    }

    internal void OnIzziCall(int n, string summary)
    {
        _izziCallCount = n;
        AddEvent($"⚡ IZZI #{n}: {summary}");
    }

    // ════════════════════════════════════════════════════════
    // Forecast Gantt
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// Triggered after each real IzziCore call.
    /// Snapshots current sim state on the tick thread, then runs a
    /// full forecast asynchronously so it never blocks the tick loop.
    /// </summary>
    internal void TriggerForecast()
    {
        // Cancel any in-flight forecast
        _forecastCts.Cancel();
        _forecastCts = new CancellationTokenSource();
        var ct = _forecastCts.Token;

        // Deep-clone on the tick thread (safe — single-threaded tick loop)
        var fState      = _state.DeepClone();
        var fClock      = _clock.Clone();
        var fEventQueue = _eventQueue.Clone();
        var fWaveIndex  = _nextWaveIndex;

        Task.Run(() =>
        {
            try
            {
                var blocks = RunForecastGantt(fState, fClock, fEventQueue, fWaveIndex, ct);
                if (!ct.IsCancellationRequested)
                    _forecastBlocks = blocks;   // atomic reference swap
            }
            catch { /* forecast failures are silent */ }
        }, ct);
    }

    private List<GanttBlockDto> RunForecastGantt(
        SimulationState   fState,
        SimulationClock   fClock,
        EventQueue        fEventQueue,
        int               fWaveIndex,
        CancellationToken ct)
    {
        var blocks = new List<GanttBlockDto>();
        var open   = new Dictionary<Guid, OpenBlock>();

        // Open a block for each resource that is already in an active state
        var prev = fState.Resources.ToDictionary(
            kv => kv.Key,
            kv => (kv.Value.CurrentState, kv.Value.CurrentQueueId));

        foreach (var (id, res) in fState.Resources)
        {
            var bt = GetBlockType(res.CurrentState);
            if (bt is null) continue;
            open[id] = new OpenBlock
            {
                Resource  = GetName(id),
                BlockType = bt,
                Queue     = res.CurrentQueueId.HasValue ? GetName(res.CurrentQueueId.Value) : null,
                Color     = GetBlockColor(res.CurrentState, res.CurrentQueueId),
                StartMs   = fClock.Now.ToUnixTimeMilliseconds(),
            };
        }

        var fCore   = IzziCoreBuilder.Build(_config.IzziDiscTime);
        var fWorker = new Worker(fState, fEventQueue, fClock, _config, fCore);

        while (fClock.Now < _load.SimEnd && !ct.IsCancellationRequested
               && (fEventQueue.HasEvents || fState.Queues.Values.Any(q => q.PendingItems.Count > 0)
                   || fState.Resources.Values.Any(r => r.CurrentState != SimResourceState.LoggedOut)))
        {
            fClock.Advance(_config.Step);

            // Inject future task waves
            while (fWaveIndex < _load.TaskWaves.Count
                   && fClock.Now >= _load.TaskWaves[fWaveIndex].At)
            {
                var wave = _load.TaskWaves[fWaveIndex++];
                foreach (var task in wave.Tasks)
                    fState.Queues[task.QueueId].PendingItems.Add(task);
            }

            // Process events
            while (fEventQueue.NextTimestamp.HasValue
                   && fEventQueue.NextTimestamp.Value <= fClock.Now)
            {
                foreach (var evt in fEventQueue.GetNextBatch())
                    evt.Apply(fState, fEventQueue);
            }

            // Detect event-driven transitions
            var afterEvents = fState.Resources.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.CurrentState, kv.Value.CurrentQueueId));
            DetectForecastTransitions(prev, afterEvents, fClock, blocks, open);
            prev = afterEvents;

            // Worker.Observe() (may change states via command execution)
            var beforeWorker = fState.Resources.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.CurrentState, kv.Value.CurrentQueueId));
            fWorker.Observe();
            var afterWorker = fState.Resources.ToDictionary(
                kv => kv.Key,
                kv => (kv.Value.CurrentState, kv.Value.CurrentQueueId));
            DetectForecastTransitions(beforeWorker, afterWorker, fClock, blocks, open);
            prev = afterWorker;
        }

        // Close any still-open blocks at end of forecast
        var endMs = fClock.Now.ToUnixTimeMilliseconds();
        foreach (var (_, ob) in open)
        {
            blocks.Add(new GanttBlockDto
            {
                Resource  = ob.Resource,
                BlockType = ob.BlockType,
                Queue     = ob.Queue,
                Color     = ob.Color,
                StartMs   = ob.StartMs,
                EndMs     = endMs,
            });
        }

        return blocks;
    }

    private void DetectForecastTransitions(
        Dictionary<Guid, (string State, Guid? QueueId)> prev,
        Dictionary<Guid, (string State, Guid? QueueId)> curr,
        SimulationClock clock,
        List<GanttBlockDto> blocks,
        Dictionary<Guid, OpenBlock> open)
    {
        var nowMs = clock.Now.ToUnixTimeMilliseconds();
        foreach (var id in prev.Keys)
        {
            var p = prev[id];
            var c = curr[id];
            if (p.State == c.State && p.QueueId == c.QueueId) continue;

            if (open.TryGetValue(id, out var ob))
            {
                blocks.Add(new GanttBlockDto
                {
                    Resource  = ob.Resource,
                    BlockType = ob.BlockType,
                    Queue     = ob.Queue,
                    Color     = ob.Color,
                    StartMs   = ob.StartMs,
                    EndMs     = nowMs,
                });
                open.Remove(id);
            }

            var bt = GetBlockType(c.State);
            if (bt is not null)
            {
                open[id] = new OpenBlock
                {
                    Resource  = GetName(id),
                    BlockType = bt,
                    Queue     = c.QueueId.HasValue ? GetName(c.QueueId.Value) : null,
                    Color     = GetBlockColor(c.State, c.QueueId),
                    StartMs   = nowMs,
                };
            }
        }
    }

    // ════════════════════════════════════════════════════════
    // State DTO + broadcasting
    // ════════════════════════════════════════════════════════

    private SimStateDto BuildStateDto()
    {
        var elapsedSec = Math.Max(1.0, (_clock.Now - _load.SimStart).TotalSeconds);
        var nowMs      = _clock.Now.ToUnixTimeMilliseconds();

        // Resources
        var resources = _state.Resources.Values.Select(r =>
        {
            var user      = r.CurrentUserId.HasValue  ? GetName(r.CurrentUserId.Value)  : null;
            var queueName = r.CurrentQueueId.HasValue ? GetName(r.CurrentQueueId.Value) : null;
            var queueColor = r.CurrentQueueId.HasValue && _queueColors.TryGetValue(r.CurrentQueueId.Value, out var c) ? c : null;
            return new ResourceDto(r.Name, r.CurrentState, queueName, user, queueColor);
        }).ToList();

        // Queues
        var queues = _state.Queues.Values.Select(q =>
        {
            var color = _queueColors.TryGetValue(q.Id, out var c) ? c : "#555";
            return new QueueDto(q.Name, q.PendingItems.Count, _tasksCompleted.GetValueOrDefault(q.Id), color);
        }).ToList();

        // Gantt (completed + in-progress blocks)
        var blocks = new List<GanttBlockDto>(_completedBlocks);
        foreach (var (_, ob) in _openBlocks)
        {
            blocks.Add(new GanttBlockDto
            {
                Resource  = ob.Resource,
                BlockType = ob.BlockType,
                Queue     = ob.Queue,
                Color     = ob.Color,
                StartMs   = ob.StartMs,
                EndMs     = nowMs,
            });
        }

        // Metrics
        var elapsedHours = elapsedSec / 3600.0;
        var tph  = _tasksCompleted.ToDictionary(kv => GetName(kv.Key), kv => Math.Round(kv.Value / elapsedHours, 1));
        var util = _utilSeconds.ToDictionary  (kv => GetName(kv.Key), kv => Math.Round(kv.Value / elapsedSec * 100.0, 1));

        // Queue colour map for JS
        var qColors = _state.Queues.Values.ToDictionary(
            q => q.Name,
            q => _queueColors.TryGetValue(q.Id, out var c) ? c : "#555");

        return new SimStateDto(
            Clock:               _clock.Now.ToString("HH:mm:ss"),
            ClockMs:             nowMs,
            StartMs:             _load.SimStart.ToUnixTimeMilliseconds(),
            EndMs:               _load.SimEnd.ToUnixTimeMilliseconds(),
            IsRunning:           _isRunning,
            IsFinished:          _isFinished,
            Speed:               _speed,
            Resources:           resources,
            Queues:              queues,
            GanttBlocks:         blocks,
            ForecastGanttBlocks: _forecastBlocks,
            Events:              _eventLog.TakeLast(50).Reverse().ToList(),
            Metrics:             new MetricsDto(tph, util),
            QueueColors:         qColors);
    }

    private async Task BroadcastStateTick()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastBroadcast).TotalMilliseconds < 16) return;
        _lastBroadcast = now;
        await _hub.Clients.All.SendAsync("StateUpdate", BuildStateDto());
    }

    public async Task BroadcastStateForce()
    {
        _lastBroadcast = DateTime.UtcNow;
        await _hub.Clients.All.SendAsync("StateUpdate", BuildStateDto());
    }

    public async Task BroadcastStateToClient(IClientProxy client) =>
        await client.SendAsync("StateUpdate", BuildStateDto());

    // ════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════

    private string GetName(Guid id) =>
        _names.TryGetValue(id, out var n) ? n : id.ToString()[..8];

    // ════════════════════════════════════════════════════════
    // IzziCore logging wrapper
    // ════════════════════════════════════════════════════════

    private class WebLoggingCore : IIzziCore
    {
        private readonly IIzziCore         _inner;
        private readonly SimulationService _svc;
        private int _n;

        public WebLoggingCore(IIzziCore inner, SimulationService svc)
        {
            _inner = inner;
            _svc   = svc;
        }

        public IEnumerable<CommandsForResource> Run(
            IReadOnlyList<UnpopulatedResource> resources,
            IReadOnlyList<IzziQueue>           queues)
        {
            _n++;
            var result = _inner.Run(resources, queues).ToList();

            var cmds = result.Count == 0
                ? "(no commands)"
                : string.Join(", ", result.Select(c =>
                {
                    var pop = (PopulatedResource)c.Resource;
                    return $"{_svc.GetName(c.Resource.Id)}→{_svc.GetName(pop.Queue.Id)}";
                }));

            _svc.OnIzziCall(_n, $"res={resources.Count} q={queues.Count}: {cmds}");
            _svc.TriggerForecast();
            return result;
        }
    }
}
