# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This repo contains two C# components that will be integrated into a single workforce automation platform (RPA + Humans + GenAI). They currently live as separate folders with no shared project file or solution:

- **`Core/`** — `IzziAutomationCore` (.NET 8 class library): the decision/optimization engine. Given current resource states and queues, it computes what command each resource should execute next.
- **`Simulator/`** — `IzziAutomationSimulator` (namespace only, no `.csproj` yet): the Discrete Event Simulation (DES) engine. It models how resources and queues evolve over time and drives the Core engine on a trigger basis.

## Architecture

### Core (Decision Engine)

`IIzziCore.Run(resources, queues)` is the single entry point. It takes a snapshot of reality and returns commands.

**Data flow inside `IzziCore.Run()`:**
1. `IResourcePopulator` (impl: `PriorityAndQueueBasedResourcePopulator`) — expands each `UnpopulatedResource × IzziQueue × Priority` into a `PopulatedResource`, which calculates `RealCapacity` (how many items the resource can process in the `discTime` window given setup overhead).
2. `IResourceTaskRedistributer` — adjusts task counts across resources to avoid over-allocation.
3. `IMostBeneficialResourceGetter` + `IResourceBenefitCalculator` (impl: `BiasedResourceBenefitCalculator`) — picks the highest-benefit assignment. Benefit formula: `RealCapacity × Queue.Weight(bias) / Priority`.
4. Greedy loop: select best, remove from pool, decrement capacity of equal-priority/same-queue siblings, repeat.
5. Each selected `PopulatedResource` calls `State.CommandsForQueue(queue)` on its `ResourceState` to get the concrete command sequence.

**Resource states (polymorphic `ResourceState` records in `Core/ResourceStates/`):**
- `LoggedOut` → commands: `[Login, ExecuteQueue]`
- `Idle(User)` → commands: `[ExecuteQueue]` if same user, else `[Logout, Login, ExecuteQueue]`
- `Working(Queue)` → commands: `[EmptyCommand]` if same queue, else switch-user or switch-queue commands
- `Stopping(Queue)` → similar to Working

**External dependencies:**
- `Afonsomtsm.Result` NuGet — provides `Option<T>` and `Result<T>` types (`.Or()`, `.Pipe()`, `.IsNone()` etc. are from this library)
- `IzziAutomationDatabase` project reference — provides `User`, `ResourceCommand` subtypes (`Login`, `Logout`, `ExecuteQueue`, `EmptyCommand`), and other DB entities. **This project is not present in this repo** — it is a dependency from the main Izzi.Web solution.

### Simulator (DES Engine)

Event-driven simulation with batch processing. All files use namespace `IzziAutomationSimulator`.

**Core loop (in `RealSimulator.Run()`):**
```
while (CanAdvance()):
    Clock.Advance(Step)
    EventQueue.GetNextBatch() → apply all events at same timestamp atomically → state updated
    Worker.Observe() → check triggers → maybe call IzziCore → schedule new events
```

**Key design decisions:**
- **Batch processing**: all events sharing the same timestamp are applied before `Worker.Observe()` runs, ensuring consistent state observation.
- **Autonomous resources**: after `SetupCompletedEvent`, resources self-schedule `ItemCompletedEvent` in a loop (`ProcessEnabled=true`) without calling Izzi per item.
- **Conservative state mapping** (`IzziStateAdapter`): transitional states (`LoggingIn→LoggedOut`, `SettingUpQueue→Idle`, `LoggingOut→Idle`) are collapsed before presenting to Core.
- **Forecast isolation**: `ForecastSimulator.FromReal()` deep-clones `SimulationState`, `SimulationClock`, and `EventQueue` so forecast runs don't affect the live simulation.

**Event types** (`SimulationEvents.cs`): `LoginCompletedEvent`, `LogoutCompletedEvent`, `SetupCompletedEvent`, `ItemCompletedEvent`. Each implements `Apply(state, eventQueue)` and `Clone()`.

**Worker triggers** for calling Izzi:
1. Timer: `IzziTimerInterval` elapsed since last call (default 10 min)
2. Idle: any resource is `Idle` with no `PendingCommands`

## Critical Integration Gap

`Worker.CallIzzi()` currently uses `IzziCoreStub.Run()` (returns empty commands). The real `IzziCore` (in `Core/`) must replace this stub. The Simulator defines its own local `UnpopulatedResource` and `IzziQueue` records (in `Worker.cs`) which duplicate the Core's types — these must be unified or bridged via `IzziStateAdapter`.

The Simulator has **no `.csproj`** — it cannot be compiled as-is. A project file targeting `net8.0` with a reference to `IzziAutomationCore.csproj` is needed.

## Build

No build scripts exist yet. Once a `.csproj` is added to `Simulator/`:

```bash
# Build Core (requires IzziAutomationDatabase sibling project)
dotnet build Core/IzziAutomationCore.csproj

# Build Simulator (once .csproj exists)
dotnet build Simulator/IzziAutomationSimulator.csproj
```

`SimulatorConfiguration.Validate()` should be called after construction for fail-fast validation.

## Key Conventions

- **`Pipe()` extension**: used pervasively in Core instead of intermediate variables — `value.Pipe(fn)` is equivalent to `fn(value)`.
- **`Option<T>`**: used for nullable/optional values (e.g., `_realCapacity`, `discTime`). Check `.IsNone()` before accessing `.Data`.
- Resource state polymorphism: never switch on state string in Core — call the virtual methods `SetupTimeOverhead()` and `CommandsForQueue()` on the `ResourceState` record.
- In the Simulator, resource state is a `string` constant (`SimResourceState.Idle`, etc.), not a type hierarchy — this is the main structural difference between the two codebases.
