using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IzziAutomationSimulator;

namespace IzziWebApp.Connectors.BluePrism;

public static class BluePrismConnector
{
    // ════════════════════════════════════════════════════════
    // Entry point
    // ════════════════════════════════════════════════════════

    public static SimulationLoadResult Load(string directory)
    {
        var resources = ParseResources(Path.Combine(directory, "BPAResource.csv"));
        var queues    = ParseQueues   (Path.Combine(directory, "BPAWorkQueue.csv"));
        var sessions  = ParseSessions (Path.Combine(directory, "BPASession.csv"));
        var items     = ParseItems    (Path.Combine(directory, "BPAWorkQueueItem.csv"));
        var configs   = ParseConfigs  (Path.Combine(directory, "izzi_queue_config.csv"));

        // ── ID maps ─────────────────────────────────────────
        // Resource GUIDs come directly from BPAResource.resourceid (valid GUIDs).
        var botNameToGuid   = resources.ToDictionary(r => r.Name, r => r.ResourceId);
        // Queue BpIds (e.g. "q0000001-...") contain non-hex chars → deterministic MD5 GUID.
        var queueBpIdToGuid = configs.ToDictionary(c => c.QueueBpId, c => MakeGuid(c.QueueBpId));
        var configByBpId    = configs.ToDictionary(c => c.QueueBpId);
        // User GUIDs — derived from izzi_user strings.
        var userGuidMap     = configs.Select(c => c.IzziUser).Distinct()
                                     .ToDictionary(u => u, u => MakeGuid("user:" + u));

        // ── Derived parameters ──────────────────────────────
        var avgLoginTime  = DeriveAvgLoginTime (sessions, botNameToGuid, configs);
        var avgLogoutTime = DeriveAvgLogoutTime(sessions, botNameToGuid, configs);
        var avgItemTime   = DeriveAvgItemTime  (items,    queueBpIdToGuid, configByBpId);
        var avgSetupTime  = DeriveAvgSetupTime (sessions, items, queueBpIdToGuid, configByBpId);

        // ── Build SimulationState (no tasks yet) ────────────
        var state = BuildInitialState(
            resources, configs,
            queueBpIdToGuid, userGuidMap,
            avgItemTime, avgSetupTime, avgLoginTime, avgLogoutTime);

        // ── Build task wave schedule ─────────────────────────
        var queueSlaMap = configs.ToDictionary(
            c => c.QueueBpId,
            c => TimeSpan.FromMinutes(c.SlaMinutes));
        var taskWaves = BuildTaskWaves(items, queueBpIdToGuid, queueSlaMap);

        // ── Timeline ─────────────────────────────────────────
        var earliestLoaded = items.Min(i => i.Loaded);
        var latestSession  = sessions.Max(s => s.EndDateTime);
        var simStart       = earliestLoaded - TimeSpan.FromMinutes(10);
        var simEnd         = latestSession  + TimeSpan.FromHours(2);

        // ── Names dictionary (for logging/UI) ───────────────
        var names = new Dictionary<Guid, string>();
        foreach (var r in resources)             names[r.ResourceId] = r.Name;
        foreach (var c in configs)
        {
            if (queueBpIdToGuid.TryGetValue(c.QueueBpId, out var qg)) names[qg] = c.QueueName;
            if (userGuidMap.TryGetValue(c.IzziUser, out var ug))       names[ug] = c.IzziUser;
        }

        return new SimulationLoadResult
        {
            InitialState = state,
            SimStart     = simStart,
            SimEnd       = simEnd,
            TaskWaves    = taskWaves,
            Names        = names,
        };
    }

    // ════════════════════════════════════════════════════════
    // Derived parameter calculations
    // ════════════════════════════════════════════════════════

    /// <summary>
    /// AVG(worktime) per queue for attempt=1 items.
    /// Falls back to config value when no data is available.
    /// </summary>
    private static Dictionary<Guid, TimeSpan> DeriveAvgItemTime(
        List<BpItem>               items,
        Dictionary<string, Guid>   queueBpIdToGuid,
        Dictionary<string, IzziQueueConfig> configByBpId)
    {
        var result = new Dictionary<Guid, TimeSpan>();

        foreach (var group in items.GroupBy(i => i.QueueIdent))
        {
            if (!queueBpIdToGuid.TryGetValue(group.Key, out var guid)) continue;

            var sample = group.Where(i => i.Attempt == 1 && i.WorkTimeSeconds > 0).ToList();
            if (sample.Any())
            {
                result[guid] = TimeSpan.FromSeconds(sample.Average(i => i.WorkTimeSeconds));
            }
            else if (configByBpId.TryGetValue(group.Key, out var cfg))
            {
                result[guid] = TimeSpan.FromSeconds(cfg.AvgItemTimeFallback);
            }
        }

        return result;
    }

    /// <summary>
    /// Per-bot AvgLoginTime = MINIMUM inter-session gap where a queue/process switch occurred.
    /// Minimum discards noise (idle time between sessions); the shortest gap is the pure login cost.
    /// Falls back to the per-queue config average when no transition is observable.
    /// </summary>
    private static Dictionary<Guid, TimeSpan> DeriveAvgLoginTime(
        List<BpSession>            sessions,
        Dictionary<string, Guid>   botNameToGuid,
        List<IzziQueueConfig>      configs)
    {
        return DeriveInterSessionGaps(sessions, botNameToGuid, configs,
            c => c.AvgLoginTimeFallback);
    }

    /// <summary>
    /// Per-bot AvgLogoutTime — same observable (inter-session gap on switch) as AvgLoginTime.
    /// Both operations complete within the gap; the minimum is the tightest bound on each.
    /// Falls back to config logout time.
    /// </summary>
    private static Dictionary<Guid, TimeSpan> DeriveAvgLogoutTime(
        List<BpSession>            sessions,
        Dictionary<string, Guid>   botNameToGuid,
        List<IzziQueueConfig>      configs)
    {
        return DeriveInterSessionGaps(sessions, botNameToGuid, configs,
            c => c.AvgLogoutTimeFallback);
    }

    /// <summary>
    /// Shared logic: for each bot, collect gaps between consecutive sessions where
    /// process or queue changed, then take the minimum.
    /// </summary>
    private static Dictionary<Guid, TimeSpan> DeriveInterSessionGaps(
        List<BpSession>                 sessions,
        Dictionary<string, Guid>        botNameToGuid,
        List<IzziQueueConfig>           configs,
        Func<IzziQueueConfig, double>   fallbackSelector)
    {
        var result = new Dictionary<Guid, TimeSpan>();
        var fallbackSec = configs.Count > 0
            ? configs.Average(fallbackSelector)
            : 30.0;

        foreach (var group in sessions.GroupBy(s => s.ResourceName))
        {
            if (!botNameToGuid.TryGetValue(group.Key, out var botGuid)) continue;

            var sorted = group.OrderBy(s => s.StartDateTime).ToList();

            var gaps = new List<TimeSpan>();
            for (int i = 0; i < sorted.Count - 1; i++)
            {
                var cur  = sorted[i];
                var next = sorted[i + 1];

                // Only count when there is a true queue/process switch (logout occurred)
                bool switched = cur.QueueIdent  != next.QueueIdent
                             || cur.ProcessName != next.ProcessName;
                if (!switched) continue;

                var gap = next.StartDateTime - cur.EndDateTime;
                if (gap > TimeSpan.Zero)
                    gaps.Add(gap);
            }

            result[botGuid] = gaps.Any()
                ? gaps.Min()
                : TimeSpan.FromSeconds(fallbackSec);
        }

        return result;
    }

    /// <summary>
    /// AVG setup time per queue.
    ///
    /// For each session: find the first item start time (fi = finished − worktime)
    /// among attempt=1 items processed by that bot during that session.
    /// Setup = fi_first − session.startdatetime.
    /// Average across all sessions for the queue.
    /// Falls back to config value when no sessions have observable items.
    /// </summary>
    private static Dictionary<Guid, TimeSpan> DeriveAvgSetupTime(
        List<BpSession>                     sessions,
        List<BpItem>                        items,
        Dictionary<string, Guid>            queueBpIdToGuid,
        Dictionary<string, IzziQueueConfig> configByBpId)
    {
        var result = new Dictionary<Guid, TimeSpan>();

        // Pre-filter: only attempt=1 items, grouped by (queueIdent, resourceName)
        var itemLookup = items
            .Where(i => i.Attempt == 1)
            .GroupBy(i => (i.QueueIdent, i.ResourceName))
            .ToDictionary(g => g.Key, g => g.ToList());

        foreach (var queueGroup in sessions.GroupBy(s => s.QueueIdent))
        {
            var bpQueueId = queueGroup.Key;
            if (!queueBpIdToGuid.TryGetValue(bpQueueId, out var queueGuid)) continue;

            var setupTimes = new List<double>();

            foreach (var session in queueGroup)
            {
                var key = (bpQueueId, session.ResourceName);
                if (!itemLookup.TryGetValue(key, out var botItems)) continue;

                // fi = moment item started being processed
                // Only consider items whose processing started within this session's time window
                var firstFi = botItems
                    .Select(i => (fi: i.Finished - TimeSpan.FromSeconds(i.WorkTimeSeconds), item: i))
                    .Where(x => x.fi >= session.StartDateTime && x.fi < session.EndDateTime)
                    .Select(x => x.fi)
                    .DefaultIfEmpty(DateTimeOffset.MinValue)
                    .Min();

                if (firstFi == DateTimeOffset.MinValue) continue;

                var setupSec = (firstFi - session.StartDateTime).TotalSeconds;
                // Sanity bounds: must be non-negative and less than 1 hour
                if (setupSec >= 0 && setupSec < 3600)
                    setupTimes.Add(setupSec);
            }

            if (setupTimes.Any())
            {
                result[queueGuid] = TimeSpan.FromSeconds(setupTimes.Average());
            }
            else if (configByBpId.TryGetValue(bpQueueId, out var cfg))
            {
                result[queueGuid] = TimeSpan.FromSeconds(cfg.AvgSetupTimeFallback);
            }
        }

        // Fill in queues that had no sessions (use config fallback)
        foreach (var cfg in configByBpId.Values)
        {
            if (!queueBpIdToGuid.TryGetValue(cfg.QueueBpId, out var qg)) continue;
            if (!result.ContainsKey(qg))
                result[qg] = TimeSpan.FromSeconds(cfg.AvgSetupTimeFallback);
        }

        return result;
    }

    // ════════════════════════════════════════════════════════
    // State + wave builders
    // ════════════════════════════════════════════════════════

    private static SimulationState BuildInitialState(
        List<BpResource>                    resources,
        List<IzziQueueConfig>               configs,
        Dictionary<string, Guid>            queueBpIdToGuid,
        Dictionary<string, Guid>            userGuidMap,
        Dictionary<Guid, TimeSpan>          avgItemTime,
        Dictionary<Guid, TimeSpan>          avgSetupTime,
        Dictionary<Guid, TimeSpan>          avgLoginTime,
        Dictionary<Guid, TimeSpan>          avgLogoutTime)
    {
        var state = new SimulationState { Name = "BluePrism-Run" };

        // Resources (bots) — all start LoggedOut
        foreach (var res in resources)
        {
            state.Resources[res.ResourceId] = new SimResourceState
            {
                Id            = res.ResourceId,
                Name          = res.Name,
                AvgLoginTime  = avgLoginTime .TryGetValue(res.ResourceId, out var lt)  ? lt  : TimeSpan.FromSeconds(30),
                AvgLogoutTime = avgLogoutTime.TryGetValue(res.ResourceId, out var lot) ? lot : TimeSpan.FromSeconds(20),
            };
        }

        // Queues — empty PendingItems; seeded FinishedTask so AverageTaskWorkTime = TMO
        foreach (var cfg in configs)
        {
            if (!queueBpIdToGuid.TryGetValue(cfg.QueueBpId, out var queueGuid)) continue;
            if (!userGuidMap.TryGetValue(cfg.IzziUser,  out var userId))    continue;

            var tmo   = avgItemTime .TryGetValue(queueGuid, out var at) ? at : TimeSpan.FromSeconds(cfg.AvgItemTimeFallback);
            var setup = avgSetupTime.TryGetValue(queueGuid, out var st) ? st : TimeSpan.FromSeconds(cfg.AvgSetupTimeFallback);

            state.Queues[queueGuid] = new SimQueueState
            {
                Id          = queueGuid,
                Name        = cfg.QueueName,
                UserId      = userId,
                SLA         = TimeSpan.FromMinutes(cfg.SlaMinutes),
                Criticality = cfg.Criticality,
                AvgSetupTime = setup,
                // Seed one completed task so Core.QueueFinishedTaskList.AverageTaskWorkTime
                // and Simulator.ScheduleNextItem both use the derived TMO, not the 3-min fallback.
                FinishedTasks = new List<SimFinishedTask>
                {
                    new SimFinishedTask
                    {
                        Id              = Guid.NewGuid(),
                        QueueId         = queueGuid,
                        ResourceId      = Guid.NewGuid(),
                        CompletedAt     = DateTimeOffset.UnixEpoch,  // MinValue would underflow in IzziStateAdapter.BuildFinishedTask (Loaded = CompletedAt - duration)
                        DurationSeconds = tmo.TotalSeconds,
                    }
                },
            };
        }

        return state;
    }

    /// <summary>
    /// Group all BPAWorkQueueItem rows by their loaded timestamp.
    /// Each unique timestamp becomes a ScheduledWave injected during the simulation tick.
    /// </summary>
    private static List<ScheduledWave> BuildTaskWaves(
        List<BpItem>                    items,
        Dictionary<string, Guid>        queueBpIdToGuid,
        Dictionary<string, TimeSpan>    queueBpIdToSla)
    {
        return items
            .Where(i => queueBpIdToGuid.ContainsKey(i.QueueIdent))
            .GroupBy(i => i.Loaded)
            .OrderBy(g => g.Key)
            .Select(g => new ScheduledWave(
                g.Key,
                g.Select(i =>
                {
                    var queueGuid = queueBpIdToGuid[i.QueueIdent];
                    var sla       = queueBpIdToSla.TryGetValue(i.QueueIdent, out var s) ? s : TimeSpan.FromHours(1);
                    return new SimTask
                    {
                        Id          = MakeGuid(i.Ident),
                        QueueId     = queueGuid,
                        CreatedAt   = i.Loaded,
                        SLADeadline = i.Loaded + sla,
                    };
                }).ToList()))
            .ToList();
    }

    // ════════════════════════════════════════════════════════
    // CSV parsers
    // ════════════════════════════════════════════════════════

    private static List<BpResource> ParseResources(string path)
    {
        return ReadCsv(path)
            .Select(f => new BpResource(
                Guid.Parse(f[0].Trim()),
                f[1].Trim()))
            .ToList();
    }

    private static List<BpQueue> ParseQueues(string path)
    {
        return ReadCsv(path)
            .Select(f => new BpQueue(
                f[0].Trim(),
                f[1].Trim(),
                f[4].Trim()))
            .ToList();
    }

    private static List<BpSession> ParseSessions(string path)
    {
        return ReadCsv(path)
            .Select(f => new BpSession(
                int.Parse(f[0].Trim()),
                f[1].Trim(),
                f[2].Trim(),
                f[3].Trim(),
                ParseDto(f[4].Trim()),
                ParseDto(f[5].Trim())))
            .ToList();
    }

    private static List<BpItem> ParseItems(string path)
    {
        return ReadCsv(path)
            .Select(f => new BpItem(
                f[0].Trim(),
                f[1].Trim(),
                f[2].Trim(),
                f[3].Trim(),
                int.Parse(f[4].Trim()),
                ParseDto(f[5].Trim()),
                ParseDto(f[6].Trim()),
                double.Parse(f[7].Trim(), CultureInfo.InvariantCulture),
                f[8].Trim()))
            .ToList();
    }

    private static List<IzziQueueConfig> ParseConfigs(string path)
    {
        return ReadCsv(path)
            .Select(f => new IzziQueueConfig(
                f[0].Trim(),
                f[1].Trim(),
                f[2].Trim(),
                int.Parse(f[3].Trim()),
                int.Parse(f[4].Trim()),
                double.Parse(f[5].Trim(), CultureInfo.InvariantCulture),
                double.Parse(f[6].Trim(), CultureInfo.InvariantCulture),
                double.Parse(f[7].Trim(), CultureInfo.InvariantCulture),
                double.Parse(f[8].Trim(), CultureInfo.InvariantCulture),
                int.Parse(f[9].Trim()),
                int.Parse(f[10].Trim()),
                bool.Parse(f[11].Trim()),
                bool.Parse(f[12].Trim())))
            .ToList();
    }

    // ════════════════════════════════════════════════════════
    // Utilities
    // ════════════════════════════════════════════════════════

    private static IEnumerable<string[]> ReadCsv(string path) =>
        File.ReadLines(path)
            .Skip(1)                              // skip header
            .Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.Split(','));

    private static DateTimeOffset ParseDto(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);

    /// <summary>
    /// Converts an arbitrary string (e.g. "q0000001-...", "user:UserA") to a
    /// stable, reproducible GUID using MD5. Not for security — purely for stable IDs.
    /// </summary>
    private static Guid MakeGuid(string seed)
    {
        var hash = MD5.HashData(Encoding.UTF8.GetBytes(seed));
        return new Guid(hash);
    }
}
