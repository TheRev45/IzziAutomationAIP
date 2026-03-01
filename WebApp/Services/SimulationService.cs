using IzziAutomationCore;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationSimulator;
using IzziWebApp.Hubs;
using Microsoft.AspNetCore.SignalR;

namespace IzziWebApp.Services;

// ═══════════════════════════════════════════════════════════════
// DTOs (sent to browser via SignalR — all camelCased by default)
// ═══════════════════════════════════════════════════════════════

public record SimStateDto(
    string   Clock,
    long     ClockMs,
    long     StartMs,
    long     EndMs,
    bool     IsRunning,
    bool     IsFinished,
    double   Speed,
    List<ResourceDto>    Resources,
    List<QueueDto>       Queues,
    List<GanttBlockDto>  GanttBlocks,
    List<string>         Events,
    MetricsDto           Metrics
);

public record ResourceDto(string Name, string State, string? Queue, string? User);
public record QueueDto(string Name, int Pending, int Completed);
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

// ═══════════════════════════════════════════════════════════════
// SimulationService
// ═══════════════════════════════════════════════════════════════

public class SimulationService
{
    private readonly IHubContext<SimulationHub> _hub;

    // ── Dataset identifiers (same as TestRunner) ────────────────
    private static readonly Guid UserAId = new("0000aaaa-0000-0000-0000-000000000001");
    private static readonly Guid UserBId = new("0000bbbb-0000-0000-0000-000000000001");
    private static readonly Guid M1Id    = new("1111aaaa-0000-0000-0000-000000000001");
    private static readonly Guid M2Id    = new("2222aaaa-0000-0000-0000-000000000001");
    private static readonly Guid M3Id    = new("3333aaaa-0000-0000-0000-000000000001");
    private static readonly Guid Q1Id    = new("11111111-0000-0000-0000-000000000001");
    private static readonly Guid Q2Id    = new("22222222-0000-0000-0000-000000000001");
    private static readonly Guid Q3Id    = new("33333333-0000-0000-0000-000000000001");
    private static readonly Guid Q4Id    = new("44444444-0000-0000-0000-000000000001");
    private static readonly Guid Q5Id    = new("55555555-0000-0000-0000-000000000001");

    private static readonly Dictionary<Guid, string> Names = new()
    {
        [UserAId] = "UserA", [UserBId] = "UserB",
        [M1Id] = "M1",  [M2Id] = "M2",  [M3Id] = "M3",
        [Q1Id] = "Q1",  [Q2Id] = "Q2",  [Q3Id] = "Q3",
        [Q4Id] = "Q4",  [Q5Id] = "Q5",
    };

    private static readonly Dictionary<Guid, (TimeSpan sla, int crit, TimeSpan setup, TimeSpan tmo, Guid user)> QDefs = new()
    {
        [Q1Id] = (TimeSpan.FromMinutes(2), 5, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), UserAId),
        [Q2Id] = (TimeSpan.FromMinutes(3), 4, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), UserAId),
        [Q3Id] = (TimeSpan.FromMinutes(5), 3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), UserAId),
        [Q4Id] = (TimeSpan.FromMinutes(3), 4, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), UserBId),
        [Q5Id] = (TimeSpan.FromMinutes(5), 2, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(4), UserBId),
    };

    private static readonly Dictionary<Guid, int> Wave1 = new() { [Q1Id]=8, [Q2Id]=6, [Q3Id]=5, [Q4Id]=7, [Q5Id]=5 };
    private static readonly Dictionary<Guid, int> Wave2 = new() { [Q1Id]=4, [Q2Id]=3, [Q3Id]=3, [Q4Id]=4, [Q5Id]=2 };

    // ── Simulation timeline ──────────────────────────────────────
    private static readonly DateTimeOffset SimStart   = new(2026, 2, 28,  8, 50, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wave1Time  = new(2026, 2, 28,  9,  0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Wave2Time  = new(2026, 2, 28,  9,  5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset SimEnd     = new(2026, 2, 28,  9, 50, 0, TimeSpan.Zero);

    // ── Queue colors (must match frontend) ──────────────────────
    private static readonly Dictionary<Guid, string> QueueColors = new()
    {
        [Q1Id] = "#e74c3c",
        [Q2Id] = "#3498db",
        [Q3Id] = "#2ecc71",
        [Q4Id] = "#f39c12",
        [Q5Id] = "#9b59b6",
    };

    // ── Live simulation state ────────────────────────────────────
    private SimulationState         _state      = null!;
    private EventQueue              _eventQueue = null!;
    private SimulationClock         _clock      = null!;
    private Worker                  _worker     = null!;
    private SimulatorConfiguration  _config     = SimulatorConfiguration.FastTest;

    // ── Control ──────────────────────────────────────────────────
    private CancellationTokenSource _cts        = new();
    private Task?                   _runTask;
    private bool                    _wave1Done;
    private bool                    _wave2Done;
    private bool                    _isRunning;
    private bool                    _isFinished;
    private double                  _speed      = 60.0;   // sim-seconds per real-second

    // ── Gantt tracking ───────────────────────────────────────────
    private readonly List<GanttBlockDto>         _completedBlocks = new();
    private readonly Dictionary<Guid, OpenBlock> _openBlocks      = new();

    // ── Event log ────────────────────────────────────────────────
    private readonly Queue<string> _eventLog     = new();
    private const int MaxLogEntries               = 100;

    // ── Metrics ──────────────────────────────────────────────────
    private readonly Dictionary<Guid, double> _utilSeconds    = new();
    private readonly Dictionary<Guid, int>    _tasksCompleted = new();

    // ── Broadcast rate-limiting ──────────────────────────────────
    private DateTime _lastBroadcast = DateTime.MinValue;

    // ── Izzi call counter (set by WebLoggingCore) ────────────────
    private int _izziCallCount;

    // ── Internal open-block record (not sent to browser) ────────
    private class OpenBlock
    {
        public string  Resource  { get; set; } = "";
        public string  BlockType { get; set; } = "";
        public string? Queue     { get; set; }
        public string  Color     { get; set; } = "";
        public long    StartMs   { get; set; }
    }

    // ════════════════════════════════════════════════════════════
    // Constructor + Init
    // ════════════════════════════════════════════════════════════

    public SimulationService(IHubContext<SimulationHub> hub)
    {
        _hub = hub;
        InitSimulation();
    }

    private void InitSimulation()
    {
        _state      = new SimulationState { Name = "WebApp-Run" };
        _eventQueue = new EventQueue();
        _clock      = new SimulationClock(SimStart);
        _wave1Done  = false;
        _wave2Done  = false;
        _isRunning  = false;
        _isFinished = false;
        _completedBlocks.Clear();
        _openBlocks.Clear();
        _eventLog.Clear();
        _utilSeconds.Clear();
        _tasksCompleted.Clear();
        _izziCallCount = 0;

        // Machines — all LoggedOut at 08:50
        foreach (var (id, name) in new[] { (M1Id,"M1"), (M2Id,"M2"), (M3Id,"M3") })
        {
            _state.Resources[id] = new SimResourceState
            {
                Id            = id,
                Name          = name,
                AvgLoginTime  = TimeSpan.FromSeconds(30),
                AvgLogoutTime = TimeSpan.FromSeconds(20),
            };
            _utilSeconds[id] = 0;
        }

        // Queues — empty until Wave-1 at 09:00 (seeded FinishedTask so AverageTaskWorkTime = TMO)
        foreach (var (qId, (sla, crit, setup, tmo, user)) in QDefs)
        {
            _state.Queues[qId] = new SimQueueState
            {
                Id           = qId,
                Name         = Names[qId],
                UserId       = user,
                SLA          = sla,
                Criticality  = crit,
                AvgSetupTime = setup,
                FinishedTasks = new List<SimFinishedTask>
                {
                    new SimFinishedTask
                    {
                        Id              = Guid.NewGuid(),
                        QueueId         = qId,
                        ResourceId      = Guid.NewGuid(),
                        CompletedAt     = Wave1Time,
                        DurationSeconds = tmo.TotalSeconds,
                    }
                },
            };
            _tasksCompleted[qId] = 0;
        }

        _config = SimulatorConfiguration.FastTest;
        var rawCore     = IzziCoreBuilder.Build(_config.IzziDiscTime);
        var loggingCore = new WebLoggingCore(rawCore, this);
        _worker = new Worker(_state, _eventQueue, _clock, _config, loggingCore);

        AddEvent($"Ready. Wave-1 @ 09:00 (Q1=8 Q2=6 Q3=5 Q4=7 Q5=5), Wave-2 @ 09:05");
    }

    // ════════════════════════════════════════════════════════════
    // Control methods (called from SimulationHub)
    // ════════════════════════════════════════════════════════════

    public async Task StartAsync()
    {
        if (_isRunning || _isFinished)
            return;

        _isRunning = true;
        _cts       = new CancellationTokenSource();
        _runTask   = Task.Run(() => RunTickLoopAsync(_cts.Token));

        AddEvent("▶ Simulation started");
        await BroadcastStateForce();
    }

    public async Task PauseAsync()
    {
        if (!_isRunning)
            return;

        _isRunning = false;
        _cts.Cancel();

        AddEvent("⏸ Simulation paused");
        await BroadcastStateForce();
    }

    public async Task ResumeAsync()
    {
        if (_isRunning || _isFinished)
            return;

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

    public void SetSpeed(double multiplier)
    {
        _speed = Math.Max(0.1, multiplier);
    }

    // ════════════════════════════════════════════════════════════
    // Tick loop
    // ════════════════════════════════════════════════════════════

    private async Task RunTickLoopAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested && _clock.Now < SimEnd)
            {
                var tickStart = DateTime.UtcNow;

                ProcessTick();

                // Early exit when all work is done
                if (_wave2Done
                    && _state.Queues.Values.All(q => q.PendingItems.Count == 0)
                    && !_eventQueue.HasEvents)
                {
                    _isFinished = true;
                    _isRunning  = false;
                    AddEvent($"✓ Simulation complete — all tasks done!");
                    await BroadcastStateForce();
                    return;
                }

                await BroadcastStateTick();

                // Speed control: delay = 1 sim-second / speed
                if (_speed > 0)
                {
                    var realDelayMs = 1000.0 / _speed;
                    var elapsed     = (DateTime.UtcNow - tickStart).TotalMilliseconds;
                    var remaining   = realDelayMs - elapsed;
                    if (remaining > 1)
                        await Task.Delay((int)remaining, ct);
                }
            }
        }
        catch (OperationCanceledException) { /* normal pause */ }
        finally
        {
            _isRunning = false;

            if (_clock.Now >= SimEnd && !_isFinished)
            {
                _isFinished = true;
                AddEvent($"Simulation window ended at {_clock.Now:HH:mm}");
                await BroadcastStateForce();
            }
        }
    }

    // ════════════════════════════════════════════════════════════
    // Per-tick processing
    // ════════════════════════════════════════════════════════════

    private void ProcessTick()
    {
        _clock.Advance(_config.Step);   // +1 second

        // Inject task waves at their scheduled times
        if (!_wave1Done && _clock.Now >= Wave1Time)
        {
            InjectWave(Wave1, "WAVE-1");
            _wave1Done = true;
        }
        if (!_wave2Done && _clock.Now >= Wave2Time)
        {
            InjectWave(Wave2, "WAVE-2");
            _wave2Done = true;
        }

        // ── Event processing ─────────────────────────────────────
        var snapBeforeEvents = SnapshotStates();

        while (_eventQueue.NextTimestamp.HasValue && _eventQueue.NextTimestamp.Value <= _clock.Now)
        {
            foreach (var evt in _eventQueue.GetNextBatch())
            {
                if (evt is ItemCompletedEvent ice)
                    RecordItemCompleted(ice);
                evt.Apply(_state, _eventQueue);
            }
        }

        DetectTransitions(snapBeforeEvents);

        // ── Worker.Observe() (may call Izzi, then executes commands) ─
        var snapBeforeWorker = SnapshotStates();
        _worker.Observe();
        DetectTransitions(snapBeforeWorker);

        // ── Update metrics ────────────────────────────────────────
        UpdateUtilization();
    }

    private void InjectWave(Dictionary<Guid, int> wave, string label)
    {
        AddEvent($"═══ {label} ═══");
        foreach (var (qId, count) in wave)
        {
            var (sla, _, _, _, _) = QDefs[qId];
            for (int i = 0; i < count; i++)
                _state.Queues[qId].PendingItems.Add(MakeTask(qId, _clock.Now, sla));
            AddEvent($"  {Names[qId]}: +{count} tasks");
        }
    }

    private void RecordItemCompleted(ItemCompletedEvent ice)
    {
        if (_tasksCompleted.ContainsKey(ice.QueueId))
            _tasksCompleted[ice.QueueId]++;

        var res = Names.TryGetValue(ice.ResourceId, out var rn) ? rn : "?";
        var q   = Names.TryGetValue(ice.QueueId,    out var qn) ? qn : "?";
        AddEvent($"✓ {res} completed {q} item");
    }

    // ════════════════════════════════════════════════════════════
    // Gantt block tracking
    // ════════════════════════════════════════════════════════════

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

            // Log transition
            var resName  = Names.TryGetValue(id, out var rn) ? rn : "?";
            var prevQ    = prev.QueueId.HasValue && Names.TryGetValue(prev.QueueId.Value, out var pq) ? $"({pq})" : "";
            var currQ    = res.CurrentQueueId.HasValue && Names.TryGetValue(res.CurrentQueueId.Value, out var cq) ? $"({cq})" : "";
            AddEvent($"  {resName}: {prev.State}{prevQ} → {res.CurrentState}{currQ}");

            // Close open block
            if (_openBlocks.TryGetValue(id, out var openBlock))
            {
                _completedBlocks.Add(new GanttBlockDto
                {
                    Resource  = openBlock.Resource,
                    BlockType = openBlock.BlockType,
                    Queue     = openBlock.Queue,
                    Color     = openBlock.Color,
                    StartMs   = openBlock.StartMs,
                    EndMs     = _clock.Now.ToUnixTimeMilliseconds(),
                });
                _openBlocks.Remove(id);
            }

            // Open new block (only for states with visual representation)
            var blockType = GetBlockType(res.CurrentState);
            if (blockType is not null)
            {
                var queueName = res.CurrentQueueId.HasValue && Names.TryGetValue(res.CurrentQueueId.Value, out var qn) ? qn : null;
                _openBlocks[id] = new OpenBlock
                {
                    Resource  = resName,
                    BlockType = blockType,
                    Queue     = queueName,
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
            SimResourceState.LoggingIn or SimResourceState.LoggingOut =>
                "#7f8c8d",
            SimResourceState.SettingUpQueue when queueId.HasValue && QueueColors.TryGetValue(queueId.Value, out var c) =>
                LightenHex(c),
            SimResourceState.Working when queueId.HasValue && QueueColors.TryGetValue(queueId.Value, out var c) =>
                c,
            _ => "#555"
        };
    }

    private static string LightenHex(string hex)
    {
        // Mix with white at 50% for "setup" variant
        if (hex.Length == 7 && hex[0] == '#')
        {
            int r = (Convert.ToInt32(hex[1..3], 16) + 220) / 2;
            int g = (Convert.ToInt32(hex[3..5], 16) + 220) / 2;
            int b = (Convert.ToInt32(hex[5..7], 16) + 220) / 2;
            return $"#{r:X2}{g:X2}{b:X2}";
        }
        return hex;
    }

    // ════════════════════════════════════════════════════════════
    // Metrics
    // ════════════════════════════════════════════════════════════

    private void UpdateUtilization()
    {
        foreach (var (id, res) in _state.Resources)
        {
            if (res.CurrentState == SimResourceState.Working)
                _utilSeconds[id] = _utilSeconds.GetValueOrDefault(id) + _config.Step.TotalSeconds;
        }
    }

    // ════════════════════════════════════════════════════════════
    // Event log
    // ════════════════════════════════════════════════════════════

    private void AddEvent(string msg)
    {
        var entry = $"[{_clock.Now:HH:mm:ss}] {msg}";
        _eventLog.Enqueue(entry);
        while (_eventLog.Count > MaxLogEntries)
            _eventLog.Dequeue();
    }

    internal void OnIzziCall(int n, string summary)
    {
        _izziCallCount = n;
        AddEvent($"⚡ IZZI #{n}: {summary}");
    }

    // ════════════════════════════════════════════════════════════
    // State DTO + broadcasting
    // ════════════════════════════════════════════════════════════

    private SimStateDto BuildStateDto()
    {
        var elapsedSec = Math.Max(1.0, (_clock.Now - SimStart).TotalSeconds);

        var resources = _state.Resources.Values.Select(r =>
        {
            var user  = r.CurrentUserId.HasValue  && Names.TryGetValue(r.CurrentUserId.Value,  out var u) ? u : null;
            var queue = r.CurrentQueueId.HasValue && Names.TryGetValue(r.CurrentQueueId.Value, out var q) ? q : null;
            return new ResourceDto(r.Name, r.CurrentState, queue, user);
        }).ToList();

        var queues = _state.Queues.Values.Select(q =>
            new QueueDto(q.Name, q.PendingItems.Count, _tasksCompleted.GetValueOrDefault(q.Id))
        ).ToList();

        // Gantt: completed blocks + current open blocks extended to clock.Now
        var nowMs  = _clock.Now.ToUnixTimeMilliseconds();
        var blocks = new List<GanttBlockDto>(_completedBlocks);
        foreach (var (id, ob) in _openBlocks)
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
        var tph = _tasksCompleted.ToDictionary(
            kv => Names[kv.Key],
            kv => Math.Round(kv.Value / elapsedHours, 1));

        var util = _utilSeconds.ToDictionary(
            kv => Names[kv.Key],
            kv => Math.Round(kv.Value / elapsedSec * 100.0, 1));

        return new SimStateDto(
            Clock:      _clock.Now.ToString("HH:mm:ss"),
            ClockMs:    nowMs,
            StartMs:    SimStart.ToUnixTimeMilliseconds(),
            EndMs:      SimEnd.ToUnixTimeMilliseconds(),
            IsRunning:  _isRunning,
            IsFinished: _isFinished,
            Speed:      _speed,
            Resources:  resources,
            Queues:     queues,
            GanttBlocks: blocks,
            Events:     _eventLog.TakeLast(50).Reverse().ToList(),
            Metrics:    new MetricsDto(tph, util)
        );
    }

    /// <summary>Rate-limited broadcast — called from tick loop (max ~60 fps).</summary>
    private async Task BroadcastStateTick()
    {
        var now = DateTime.UtcNow;
        if ((now - _lastBroadcast).TotalMilliseconds < 16)
            return;
        _lastBroadcast = now;
        await _hub.Clients.All.SendAsync("StateUpdate", BuildStateDto());
    }

    /// <summary>Force broadcast — for control events (start/pause/reset/finish).</summary>
    public async Task BroadcastStateForce()
    {
        _lastBroadcast = DateTime.UtcNow;
        await _hub.Clients.All.SendAsync("StateUpdate", BuildStateDto());
    }

    /// <summary>Sends current state to a single caller (on connect).</summary>
    public async Task BroadcastStateToClient(IClientProxy client)
    {
        await client.SendAsync("StateUpdate", BuildStateDto());
    }

    // ════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════

    private static SimTask MakeTask(Guid queueId, DateTimeOffset createdAt, TimeSpan sla) =>
        new SimTask { Id = Guid.NewGuid(), QueueId = queueId, CreatedAt = createdAt, SLADeadline = createdAt + sla };

    // ════════════════════════════════════════════════════════════
    // IzziCore wrapper — logs each Izzi call to event log
    // ════════════════════════════════════════════════════════════

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

            string Nm(Guid id) => Names.TryGetValue(id, out var v) ? v : id.ToString()[..8];

            var cmds = result.Count == 0
                ? "(no commands)"
                : string.Join(", ", result.Select(c =>
                {
                    var pop = (PopulatedResource)c.Resource;
                    return $"{Nm(c.Resource.Id)}→{Nm(pop.Queue.Id)}";
                }));

            _svc.OnIzziCall(_n, $"res={resources.Count} q={queues.Count}: {cmds}");
            return result;
        }
    }
}
