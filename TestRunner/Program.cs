using IzziAutomationCore;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationSimulator;

// ══════════════════════════════════════════════════════════════════
// FIXED IDENTIFIERS
// ══════════════════════════════════════════════════════════════════
var userAId = new Guid("0000aaaa-0000-0000-0000-000000000001");
var userBId = new Guid("0000bbbb-0000-0000-0000-000000000001");
var m1Id    = new Guid("1111aaaa-0000-0000-0000-000000000001");
var m2Id    = new Guid("2222aaaa-0000-0000-0000-000000000001");
var m3Id    = new Guid("3333aaaa-0000-0000-0000-000000000001");
var q1Id    = new Guid("11111111-0000-0000-0000-000000000001");
var q2Id    = new Guid("22222222-0000-0000-0000-000000000001");
var q3Id    = new Guid("33333333-0000-0000-0000-000000000001");
var q4Id    = new Guid("44444444-0000-0000-0000-000000000001");
var q5Id    = new Guid("55555555-0000-0000-0000-000000000001");

// ══════════════════════════════════════════════════════════════════
// NAME MAP
// ══════════════════════════════════════════════════════════════════
var names = new Dictionary<Guid, string>
{
    [userAId] = "UserA", [userBId] = "UserB",
    [m1Id]    = "M1",    [m2Id]    = "M2",    [m3Id]    = "M3",
    [q1Id]    = "Q1",    [q2Id]    = "Q2",    [q3Id]    = "Q3",
    [q4Id]    = "Q4",    [q5Id]    = "Q5",
};

string N(Guid id) => names.TryGetValue(id, out var n) ? n : id.ToString()[..8];

// ══════════════════════════════════════════════════════════════════
// TIME SETUP
// ══════════════════════════════════════════════════════════════════
var startTime = new DateTimeOffset(2026, 2, 28, 9, 0, 0, TimeSpan.Zero);
var endTime   = startTime.AddMinutes(40);
var injectAt  = startTime.AddMinutes(5);

// ══════════════════════════════════════════════════════════════════
// QUEUE DEFINITIONS  (SLA, criticality, setupTime, avgItemTMO, owner)
// ══════════════════════════════════════════════════════════════════
var qDefs = new Dictionary<Guid, (TimeSpan sla, int crit, TimeSpan setup, TimeSpan tmo, Guid user)>
{
    [q1Id] = (TimeSpan.FromMinutes(2), 5, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), userAId),
    [q2Id] = (TimeSpan.FromMinutes(3), 4, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), userAId),
    [q3Id] = (TimeSpan.FromMinutes(5), 3, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(3), userAId),
    [q4Id] = (TimeSpan.FromMinutes(3), 4, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), userBId),
    [q5Id] = (TimeSpan.FromMinutes(5), 2, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(4), userBId),
};

var wave1 = new Dictionary<Guid, int> { [q1Id]=8, [q2Id]=6, [q3Id]=5, [q4Id]=7, [q5Id]=5 };
var wave2 = new Dictionary<Guid, int> { [q1Id]=4, [q2Id]=3, [q3Id]=3, [q4Id]=4, [q5Id]=2 };

// ══════════════════════════════════════════════════════════════════
// BUILD SIMULATION STATE
// ══════════════════════════════════════════════════════════════════
var state = new SimulationState { Name = "TestRun-2026-02-28" };

// Machines — all LoggedOut at 09:00
foreach (var (id, tag) in new[] { (m1Id, "M1"), (m2Id, "M2"), (m3Id, "M3") })
{
    state.Resources[id] = new SimResourceState
    {
        Id            = id,
        Name          = tag,
        AvgLoginTime  = TimeSpan.FromSeconds(30),
        AvgLogoutTime = TimeSpan.FromSeconds(20),
        // CurrentState defaults to "LoggedOut"
    };
}

// Queues — wave-1 tasks + 1 seeded FinishedTask so Core's AverageTaskWorkTime = TMO
foreach (var (qId, (sla, crit, setup, tmo, user)) in qDefs)
{
    var q = new SimQueueState
    {
        Id          = qId,
        Name        = N(qId),
        UserId      = user,
        SLA         = sla,
        Criticality = crit,
        AvgSetupTime = setup,
        // Seed one historical record so both Core (QueueFinishedTaskList.AverageTaskWorkTime)
        // and Simulator (SetupCompletedEvent.ScheduleNextItem) use the correct TMO.
        FinishedTasks = new List<SimFinishedTask>
        {
            new SimFinishedTask
            {
                Id              = Guid.NewGuid(),
                QueueId         = qId,
                ResourceId      = Guid.NewGuid(),
                CompletedAt     = startTime,
                DurationSeconds = tmo.TotalSeconds,
            }
        },
    };

    for (int i = 0; i < wave1[qId]; i++)
        q.PendingItems.Add(MakeTask(qId, startTime, sla));

    state.Queues[qId] = q;
}

// ══════════════════════════════════════════════════════════════════
// INFRASTRUCTURE
// ══════════════════════════════════════════════════════════════════
var clock      = new SimulationClock(startTime);
var eventQueue = new EventQueue();
var config     = SimulatorConfiguration.FastTest;   // Step=1s · IzziTimer=10min · DiscTime=10min · Speed=0
config.Validate();

var rawCore = IzziCoreBuilder.Build(config.IzziDiscTime);
var logCore = new LoggingCore(rawCore, clock, names);
var worker  = new Worker(state, eventQueue, clock, config, logCore);

// ══════════════════════════════════════════════════════════════════
// BANNER
// ══════════════════════════════════════════════════════════════════
const string Bar = "══════════════════════════════════════════════════════════════════════";
Console.WriteLine(Bar);
Console.WriteLine("  IZZI AUTOMATION — SIMULATION TEST RUN");
Console.WriteLine(Bar);
Console.WriteLine($"  Window   : {startTime:HH:mm} → {endTime:HH:mm}  (40 min)");
Console.WriteLine($"  Machines : M1  M2  M3 — all LoggedOut @ 09:00");
Console.WriteLine($"  Users    : UserA (Q1/Q2/Q3)   UserB (Q4/Q5)");
Console.WriteLine($"  Login=30s  Logout=20s  IzziTimer=10min  DiscTime=10min");
Console.WriteLine($"  Wave-1   : Q1={wave1[q1Id]} Q2={wave1[q2Id]} Q3={wave1[q3Id]} Q4={wave1[q4Id]} Q5={wave1[q5Id]}  @ 09:00");
Console.WriteLine($"  Wave-2   : Q1={wave2[q1Id]} Q2={wave2[q2Id]} Q3={wave2[q3Id]} Q4={wave2[q4Id]} Q5={wave2[q5Id]}  @ 09:05");
Console.WriteLine($"  Q1 crit=5 SLA=2m TMO=1m  Q2 crit=4 SLA=3m TMO=2m  Q3 crit=3 SLA=5m TMO=3m");
Console.WriteLine($"  Q4 crit=4 SLA=3m TMO=2m  Q5 crit=2 SLA=5m TMO=4m");
Console.WriteLine(Bar);
Console.WriteLine();

// ══════════════════════════════════════════════════════════════════
// RUN TRACKING
// ══════════════════════════════════════════════════════════════════
bool wave2Done   = false;
int  totalDone   = 0;
var  doneByQueue = qDefs.Keys.ToDictionary(k => k, _ => 0);

// ══════════════════════════════════════════════════════════════════
// MAIN LOOP  09:00 → 09:40
// ══════════════════════════════════════════════════════════════════
while (clock.Now < endTime)
{
    clock.Advance(config.Step);   // 1-second tick

    // ── Wave-2 injection at 09:05 ────────────────────────────────
    if (!wave2Done && clock.Now >= injectAt)
    {
        Console.WriteLine($"\n[{clock.Now:HH:mm:ss}]  ═══ WAVE-2 TASK INJECTION ═══");
        foreach (var (qId, count) in wave2)
        {
            var (sla, _, _, _, _) = qDefs[qId];
            for (int i = 0; i < count; i++)
                state.Queues[qId].PendingItems.Add(MakeTask(qId, clock.Now, sla));
            Console.WriteLine($"[{clock.Now:HH:mm:ss}]    {N(qId)}  +{count} tasks → {state.Queues[qId].PendingItems.Count} pending");
        }
        wave2Done = true;
        Console.WriteLine();
    }

    // ── Event processing ─────────────────────────────────────────
    // Snapshot states before, then process the current tick's batch.
    var snap1 = Snap(state);

    while (eventQueue.NextTimestamp.HasValue && eventQueue.NextTimestamp.Value <= clock.Now)
    {
        foreach (var evt in eventQueue.GetNextBatch())
        {
            if (evt is ItemCompletedEvent ice)
            {
                // Log completion before Apply() removes it from PendingItems
                var res = state.Resources[ice.ResourceId];
                var dur = ice.Timestamp - res.LastItemStart!.Value;
                var rem = state.Queues[ice.QueueId].PendingItems.Count - 1;
                totalDone++;
                doneByQueue[ice.QueueId]++;
                Console.WriteLine(
                    $"[{clock.Now:HH:mm:ss}]  DONE  {N(ice.QueueId)} item by {N(ice.ResourceId)}" +
                    $"  dur={dur.TotalSeconds:F0}s  remaining-after={rem}");
            }
            evt.Apply(state, eventQueue);
        }
    }

    PrintTrans(clock.Now, state, snap1, "EVT");

    // ── Worker.Observe(): checks triggers → maybe calls Izzi → executes pending cmds ──
    var snap2 = Snap(state);
    worker.Observe();
    PrintTrans(clock.Now, state, snap2, "CMD");

    // ── Early exit when all work is done ─────────────────────────
    if (wave2Done
        && state.Queues.Values.All(q => q.PendingItems.Count == 0)
        && !eventQueue.HasEvents)
    {
        Console.WriteLine($"\n[{clock.Now:HH:mm:ss}]  All queues empty — simulation complete early");
        break;
    }
}

// ══════════════════════════════════════════════════════════════════
// FINAL SUMMARY  @ 09:40 (or earlier if all done)
// ══════════════════════════════════════════════════════════════════
Console.WriteLine();
Console.WriteLine(Bar);
Console.WriteLine($"  FINAL SUMMARY  @ {clock.Now:HH:mm:ss}");
Console.WriteLine(Bar);
Console.WriteLine($"  Total items completed : {totalDone}");
Console.WriteLine($"  Total items remaining : {state.Queues.Values.Sum(q => q.PendingItems.Count)}");
Console.WriteLine();
Console.WriteLine("  Queue results:");
foreach (var (qId, qState) in state.Queues)
{
    var (sla, crit, _, tmo, user) = qDefs[qId];
    Console.WriteLine(
        $"    {N(qId),-3}  user={N(user),-6}  crit={crit}  SLA={sla.TotalMinutes:0}m  TMO={tmo.TotalMinutes:0}m" +
        $"  done={doneByQueue[qId],-3}  pending={qState.PendingItems.Count}");
}
Console.WriteLine();
Console.WriteLine("  Machine final state:");
foreach (var (mId, mState) in state.Resources)
{
    var userStr  = mState.CurrentUserId.HasValue  ? N(mState.CurrentUserId.Value)  : "—";
    var queueStr = mState.CurrentQueueId.HasValue ? N(mState.CurrentQueueId.Value) : "—";
    Console.WriteLine(
        $"    {N(mId),-3}  state={mState.CurrentState,-16}  user={userStr,-7}  queue={queueStr}");
}
Console.WriteLine(Bar);

// ══════════════════════════════════════════════════════════════════
// LOCAL HELPERS
// ══════════════════════════════════════════════════════════════════

SimTask MakeTask(Guid queueId, DateTimeOffset createdAt, TimeSpan sla) => new SimTask
{
    Id           = Guid.NewGuid(),
    QueueId      = queueId,
    CreatedAt    = createdAt,
    SLADeadline  = createdAt + sla,
};

Dictionary<Guid, string> Snap(SimulationState s) =>
    s.Resources.ToDictionary(kv => kv.Key, kv => kv.Value.CurrentState);

void PrintTrans(DateTimeOffset t, SimulationState s, Dictionary<Guid, string> before, string tag)
{
    foreach (var (id, prev) in before)
    {
        var curr = s.Resources[id].CurrentState;
        if (curr != prev)
            Console.WriteLine($"[{t:HH:mm:ss}]  {tag,-4}  {N(id)}: {prev} → {curr}");
    }
}

// ══════════════════════════════════════════════════════════════════
// LOGGING IZZICORE WRAPPER
// Intercepts IIzziCore.Run() to log full input/output at each call.
// ══════════════════════════════════════════════════════════════════
class LoggingCore : IIzziCore
{
    private readonly IIzziCore                _inner;
    private readonly SimulationClock          _clock;
    private readonly Dictionary<Guid, string> _names;
    private int _n;

    public LoggingCore(IIzziCore inner, SimulationClock clock, Dictionary<Guid, string> names)
    {
        _inner = inner;
        _clock = clock;
        _names = names;
    }

    private string Nm(Guid id) => _names.TryGetValue(id, out var v) ? v : id.ToString()[..8];

    public IEnumerable<CommandsForResource> Run(
        IReadOnlyList<UnpopulatedResource> resources,
        IReadOnlyList<IzziQueue>           queues)
    {
        _n++;
        const string rule = "────────────────────────────────────────────────────────────────────";
        Console.WriteLine($"\n[{_clock.Now:HH:mm:ss}]  ┌{rule}");
        Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │ IZZI CALL #{_n}");

        Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │ Resources ({resources.Count}):");
        foreach (var r in resources)
            Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │   {Nm(r.Id),-4}  state={r.State.GetType().Name}");

        Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │ Queues ({queues.Count}):");
        foreach (var q in queues)
            Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │   {Nm(q.Id),-3}  tasks={q.Tasks.Count(),-3}  crit={q.Parameters.Criticality}  SLA={q.Parameters.Sla.TotalMinutes:0}m");

        var result = _inner.Run(resources, queues).ToList();

        Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │ Commands ({result.Count}):");
        if (result.Count == 0)
            Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │   (none — all queues empty or no capacity)");
        foreach (var cmd in result)
        {
            var pop  = (PopulatedResource)cmd.Resource;
            var cmds = string.Join(" → ", cmd.Command.Select(c => c.GetType().Name));
            Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  │   {Nm(cmd.Resource.Id),-4}  → {Nm(pop.Queue.Id),-3}  [{cmds}]");
        }

        Console.WriteLine($"[{_clock.Now:HH:mm:ss}]  └{rule}");
        return result;
    }
}
