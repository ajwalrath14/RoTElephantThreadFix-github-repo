# RoT Elephant Same-Tick Flush Design

## Problem

RoTElephantThreadFix v1.0.1 correctly removes `Agent.RegisterBlow` from
`RoTElephantAgentComponent.OnTickParallel`, but it replays the captured blow
from `RoTElephantMissionLogic.OnMissionTick`. Bannerlord calls mission behavior
ticks before `TickAgentsAndTeamsImp`, so that hook necessarily replays a blow
from an earlier simulation step.

The 2026-08-25 crash dump proves the resulting failure path:

- Bannerlord `v1.4.8.119303`, Realm of Thrones `v8.1.8`, and
  RoTElephantThreadFix `v1.0.1` were loaded.
- OS thread `0x1A30` threw `TargetInvocationException` from
  `DeferredElephantBlows.Flush()` while reflecting into `Agent.RegisterBlow`.
- The inner exception was an `AccessViolationException` reading address
  `0x20` in `TaleWorlds.Native.dll`.
- The mission was fast-forwarding (`_isFastForward = 1`) with synchronous AI
  ticking (`doAsyncAITick = false`) and was mid-tick (`tickCompleted = 0`).
- The victim wrapper was still associated with the current mission and was
  neither deleted nor removed. The dump did not contain the pointed-to native
  heap page, so deeper native-pointer validity cannot be proven.
- The concurrent queue was logically empty after dequeue. The 32 values shown
  by SOS were backing-array slots, not a live backlog.

This evidence supports a timing defect rather than a queue-depth defect: a
native blow payload created during one agent-tick phase is replayed during a
later mission step.

## Approved behavior

Version 1.0.2 keeps the thread handoff but shortens it to the synchronization
boundary in the same `TickAgentsAndTeamsImp` call:

1. RoT's parallel agent component calls `QueueRegisterBlow` with the unchanged
   evaluation-stack signature.
2. `TWParallel.For` completes, so no producer is still running.
3. Bannerlord completes its serial agent and team ticks.
4. Bannerlord invokes each submodule's `AfterAsyncTickTick` hook.
5. `SubModule.AfterAsyncTickTick` drains the queue before the next mission
   simulation step begins.

This hook runs for both asynchronous and synchronous/fast-forward agent ticks.
It is preferable to transpiling Bannerlord's internal loop because it uses the
engine's supported post-agent-tick lifecycle boundary.

## Defensive eligibility checks

Each queued item records the victim's managed `Mission` reference when it is
created. Before touching native agent state, `Flush` must check in this order:

1. `Mission.Current` is not null.
2. The queued source mission is the same object as `Mission.Current`.
3. The victim is not null.
4. The victim's current managed `Mission` is the same object.
5. `victim.IsActive()` is true.

The managed mission checks must precede `IsActive()` because Bannerlord's
`Agent.Clear()` sets `Mission` to null before clearing the native state pointer
that `IsActive()` dereferences.

Eligible blows call `victim.RegisterBlow` directly with the captured value
types. The v1.0.1 `MethodInfo.Invoke` path, object-array allocation, boxing, and
`TargetInvocationException` wrapper are removed. The transpiler remains strict:
it must find exactly one compatible `Agent.RegisterBlow` call in RoT's
`OnTickParallel` method.

## Compatibility and release scope

- Bannerlord compile references remain version-locked to `1.4.8.119303`.
- Harmony remains `2.4.2`.
- The project remains `.NET Framework 4.7.2` / `net472`.
- No `ROT.dll`, local game assemblies, or Harmony runtime DLL are distributed.
- Project, module, workflow artifact, package filename, tests, and README move
  from `1.0.1` to `1.0.2`.
- The install-ready archive retains the exact two-file Bannerlord layout.

## Verification

A dependency-free .NET behavior harness compiles the real production source
against small TaleWorlds/Harmony test doubles. It must demonstrate that:

- `AfterAsyncTickTick` flushes a queued blow.
- A blow from another mission is dropped before `IsActive()` is queried.
- An inactive victim in the current mission is dropped.

Repository tests verify release metadata and packaging. Final verification
restores public NuGet references, builds the release DLL, runs both test suites,
creates the deterministic ZIP, inspects its layout, and verifies assembly and
module versions. In-game reproduction remains the final runtime validation.
