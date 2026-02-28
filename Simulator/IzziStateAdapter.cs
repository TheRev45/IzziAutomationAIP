using IzziAutomationCore.IzziTasks.Entities;
using IzziAutomationCore.Queues.Entities;
using IzziAutomationCore.Resources.Entities;
using IzziAutomationCore.ResourceStates.Entities;
using IzziAutomationDatabase.Entities;
using IzziAutomationDatabase.Entities.ResourceCommands;

namespace IzziAutomationSimulator;

/// <summary>
/// Converts between SimulationState types and IzziAutomationCore types.
///
/// Direction 1 — ToCore():
///   SimulationState → (List&lt;UnpopulatedResource&gt;, List&lt;IzziQueue&gt;) for IIzziCore.Run().
///
/// Direction 2 — ToSimCommands():
///   IEnumerable&lt;CommandsForResource&gt; → per-resource pending-command lists for Worker.
///
/// State mapping is conservative (preserves original IzziStateAdapter semantics):
///   LoggingIn     → LoggedOut  (resource not yet ready)
///   LoggingOut    → Idle       (user still active)
///   SettingUpQueue → Idle      (almost ready, not yet)
///
/// Circular reference resolution for IzziQueue ↔ IzziTask:
///   IzziQueue takes IEnumerable&lt;IzziTask&gt;; IzziTask takes IzziQueue.
///   Resolution: pass a mutable List&lt;IzziTask&gt; to the IzziQueue constructor,
///   then build tasks (which reference the queue) and add them to the list.
///   IzziQueue stores the reference, so Tasks reflects the populated list.
/// </summary>
internal static class IzziStateAdapter
{
    /// <summary>
    /// Converts the current SimulationState into types expected by IIzziCore.Run().
    /// Queues are built before resources because Working/Stopping states reference them.
    /// </summary>
    public static (List<UnpopulatedResource> resources, List<IzziQueue> queues)
        ToCore(SimulationState state)
    {
        // Step 1: build Core queues first — resource states may reference them.
        var coreQueues = state.Queues.Values
            .Select(BuildCoreQueue)
            .ToList();

        var queueById = coreQueues.ToDictionary(q => q.Id);

        // Step 2: build Core resources (Working/Stopping need queue references).
        var coreResources = state.Resources.Values
            .Select(r => BuildCoreResource(r, queueById))
            .ToList();

        return (coreResources, coreQueues);
    }

    /// <summary>
    /// Translates Core's output back into per-resource pending-command lists.
    /// Returns a dictionary mapping ResourceId → list of Simulator command objects
    /// (LoginCommand, LogoutCommand, StartProcessCommand).
    ///
    /// CommandsForResource.Resource is always a PopulatedResource at runtime
    /// (IzziCore.Run() constructs it that way), so the cast is safe.
    /// </summary>
    public static Dictionary<Guid, List<object>> ToSimCommands(
        IEnumerable<CommandsForResource> coreCommands)
    {
        var result = new Dictionary<Guid, List<object>>();

        foreach (var cmdSet in coreCommands)
        {
            var populated     = (PopulatedResource)cmdSet.Resource;
            var targetQueueId = populated.Queue.Id;
            var targetUserId  = populated.Queue.User.Id;

            var simCommands = new List<object>();
            foreach (var cmd in cmdSet.Command)
            {
                switch (cmd)
                {
                    case Login:
                        simCommands.Add(new LoginCommand(targetUserId));
                        break;
                    case Logout:
                        simCommands.Add(new LogoutCommand());
                        break;
                    case ExecuteQueue:
                        simCommands.Add(new StartProcessCommand(targetQueueId));
                        break;
                    case EmptyCommand:
                        break; // resource already working the correct queue — no action
                }
            }

            result[cmdSet.Resource.Id] = simCommands;
        }

        return result;
    }

    // -------------------------------------------------------------------------
    // Queue conversion
    // -------------------------------------------------------------------------

    private static IzziQueue BuildCoreQueue(SimQueueState sim)
    {
        var user = new User { Id = sim.UserId };

        var parameters = new ConfigurableQueueParameters(
            sla:          sim.SLA,
            criticality:  sim.Criticality,
            minResources: 0,
            maxResources: int.MaxValue,
            forceMax:     false,
            mustRun:      false);

        var finishedTaskList = new QueueFinishedTaskList(
            sim.FinishedTasks
               .Select(ft => BuildFinishedTask(ft, sim.Id))
               .ToList());

        // Two-pass to break the IzziQueue ↔ IzziTask circular reference:
        //   Pass 1: create IzziQueue with an empty (but mutable) task list.
        //   Pass 2: create IzziTask objects that reference the queue,
        //           then populate the list — IzziQueue.Tasks reflects the result.
        var taskList  = new List<IzziTask>();
        var coreQueue = new IzziQueue(
            sim.Id, sim.Name, user, parameters, sim.AvgSetupTime, taskList, finishedTaskList);

        var tasks = sim.PendingItems
            .Select(st => new IzziTask(
                Id:              st.Id,
                Queue:           coreQueue,
                Loaded:          st.CreatedAt,
                Attempt:         1,
                AttemptWorkTime: TimeSpan.Zero,
                Priority:        1))
            .ToList();

        taskList.AddRange(tasks);

        return coreQueue;
    }

    private static FinishedTask BuildFinishedTask(SimFinishedTask sim, Guid queueId)
    {
        var duration = TimeSpan.FromSeconds(sim.DurationSeconds);
        return new FinishedTask
        {
            WorkTime         = duration,
            AttemptWorkTime  = TimeSpan.Zero,
            Finished         = sim.CompletedAt,
            Loaded           = sim.CompletedAt - duration, // best approximation without original load time
            Queue            = new Queue { Id = queueId }
        };
    }

    // -------------------------------------------------------------------------
    // Resource conversion
    // -------------------------------------------------------------------------

    private static UnpopulatedResource BuildCoreResource(
        SimResourceState sim,
        Dictionary<Guid, IzziQueue> queueById)
    {
        var loginTimes    = new ResourceLoginTimes(sim.AvgLoginTime, sim.AvgLogoutTime);
        var lastItemStart = sim.LastItemStart ?? DateTimeOffset.UtcNow;
        var state         = BuildResourceState(sim, queueById);

        return new UnpopulatedResource(sim.Id, state, loginTimes, lastItemStart);
    }

    private static ResourceState BuildResourceState(
        SimResourceState sim,
        Dictionary<Guid, IzziQueue> queueById)
    {
        switch (sim.CurrentState)
        {
            case SimResourceState.LoggedOut:
            case SimResourceState.LoggingIn:    // conservative: not yet ready
                return new LoggedOut();

            case SimResourceState.Idle:
            case SimResourceState.LoggingOut:   // conservative: user still active
            case SimResourceState.SettingUpQueue: // conservative: almost ready
                return new Idle(new User { Id = sim.CurrentUserId!.Value });

            case SimResourceState.Working:
                return new Working(queueById[sim.CurrentQueueId!.Value]);

            default:
                return new LoggedOut();
        }
    }
}
