# RoT Elephant Same-Tick Flush Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Release RoTElephantThreadFix v1.0.2 so elephant blows leave RoT's parallel agent loop but execute at Bannerlord's same-tick post-agent synchronization boundary.

**Architecture:** Preserve the existing `ConcurrentQueue<PendingBlow>` and strict RoT transpiler. Capture the originating mission with each value payload, drain from `MBSubModuleBase.AfterAsyncTickTick`, validate managed mission identity before native activity state, and invoke `Agent.RegisterBlow` directly.

**Tech Stack:** C# 7.3, .NET Framework 4.7.2, Bannerlord.ReferenceAssemblies 1.4.8.119303, Harmony 2.4.2, .NET 8 behavior harness, Python 3 unittest packaging tests.

**Spec:** `docs/superpowers/specs/2026-08-24-rot-elephant-same-tick-flush-design.md`

## Global Constraints

- The release version is exactly `1.0.2` / `v1.0.2` everywhere user-visible or assembly-visible.
- Bannerlord compile references remain exactly `1.4.8.119303`, Harmony remains exactly `2.4.2`, and the target framework remains exactly `net472`.
- `QueueRegisterBlow(Agent victim, Blow blow, ref AttackCollisionData collisionData)` keeps its exact signature so the replacement IL stack remains compatible.
- The RoT transpiler still resolves RoT types by name, redistributes no proprietary assembly, and throws unless it replaces exactly one compatible call.
- Deferred blows flush only from `SubModule.AfterAsyncTickTick`, never from `RoTElephantMissionLogic.OnMissionTick`.
- Managed mission identity checks occur before `Agent.IsActive()` and before `Agent.RegisterBlow`.
- The install archive contains only `RoTElephantThreadFix/SubModule.xml` and `RoTElephantThreadFix/bin/Win64_Shipping_Client/RoTElephantThreadFix.dll`.

---

### Task 1: Same-tick dispatcher regression and implementation

**Files:**
- Create: `tests/BehaviorHarness/BehaviorHarness.csproj`
- Create: `tests/BehaviorHarness/Stubs.cs`
- Create: `tests/BehaviorHarness/Program.cs`
- Modify: `src/RoTElephantThreadFix/RoTElephantThreadFix.cs`

**Interfaces:**
- Consumes: Bannerlord's `MBSubModuleBase.AfterAsyncTickTick(float dt)` lifecycle callback.
- Produces: `SubModule.AfterAsyncTickTick(float dt)`, `PendingBlow.SourceMission`, unchanged `QueueRegisterBlow(Agent, Blow, ref AttackCollisionData)`, and `DeferredElephantBlows.Flush()`.

- [ ] **Step 1: Add a dependency-free behavior harness around the real production source**

Create `BehaviorHarness.csproj` with the production file linked into the test executable:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="Program.cs" />
    <Compile Include="Stubs.cs" />
    <Compile Include="../../src/RoTElephantThreadFix/RoTElephantThreadFix.cs" Link="RoTElephantThreadFix.cs" />
  </ItemGroup>
</Project>
```

`Stubs.cs` must define the complete members the linked source consumes:

```csharp
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute { }

    public sealed class Harmony
    {
        public Harmony(string id) { }
        public void PatchAll(Assembly assembly) { }
    }

    public static class AccessTools
    {
        public static Type TypeByName(string name) { return null; }
        public static IEnumerable<MethodInfo> GetDeclaredMethods(Type type)
        {
            return type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                                   BindingFlags.Instance | BindingFlags.Static |
                                   BindingFlags.DeclaredOnly);
        }
        public static MethodInfo Method(Type type, string name)
        {
            return type.GetMethod(name, BindingFlags.Public |
                                        BindingFlags.NonPublic |
                                        BindingFlags.Instance |
                                        BindingFlags.Static);
        }
    }

    public struct ExceptionBlock { }

    public sealed class CodeInstruction
    {
        public OpCode opcode;
        public object operand;
        public readonly List<Label> labels = new List<Label>();
        public readonly List<ExceptionBlock> blocks = new List<ExceptionBlock>();

        public CodeInstruction(OpCode opcode, object operand)
        {
            this.opcode = opcode;
            this.operand = operand;
        }
    }
}

namespace TaleWorlds.MountAndBlade
{
    public abstract class MBSubModuleBase
    {
        protected virtual void OnSubModuleLoad() { }
        protected virtual void AfterAsyncTickTick(float dt) { }
    }

    public sealed class Mission
    {
        public static Mission Current { get; set; }
    }

    public struct Blow { public int Marker; }
    public struct AttackCollisionData { public int Marker; }

    public sealed class Agent
    {
        public Mission Mission { get; set; }
        public bool Active { get; set; }
        public bool ThrowWhenActivityIsRead { get; set; }
        public int ActivityReads { get; private set; }
        public int RegisteredBlows { get; private set; }

        public bool IsActive()
        {
            ActivityReads++;
            if (ThrowWhenActivityIsRead)
                throw new InvalidOperationException("IsActive must not be read");
            return Active;
        }

        public void RegisterBlow(Blow blow, in AttackCollisionData collisionData)
        {
            RegisteredBlows++;
        }
    }
}
```

`Program.cs` must run three literal behavioral cases, set the v1.0.1
`OriginalRegisterBlow` field through reflection when it exists, and return a
nonzero exit code on any failed assertion:

```csharp
using System;
using System.Reflection;
using RoTElephantThreadFix;
using TaleWorlds.MountAndBlade;

internal static class Program
{
    private static int _failures;

    private static int Main()
    {
        ConfigureLegacyReflectionTargetIfPresent();
        SameTickHookFlushesQueuedBlow();
        CrossMissionBlowIsDroppedBeforeActivityRead();
        InactiveVictimIsDropped();
        Console.WriteLine(_failures == 0 ? "Behavior harness passed: 3/3" :
                                          "Behavior harness failed: " + _failures);
        return _failures == 0 ? 0 : 1;
    }

    private static void SameTickHookFlushesQueuedBlow()
    {
        Mission mission = new Mission();
        Mission.Current = mission;
        Agent victim = NewAgent(mission, true);
        Enqueue(victim);

        MethodInfo hook = typeof(SubModule).GetMethod(
            "AfterAsyncTickTick", BindingFlags.Instance | BindingFlags.NonPublic |
                                   BindingFlags.DeclaredOnly);
        if (hook == null)
        {
            Fail("AfterAsyncTickTick override is missing");
            DeferredElephantBlows.Flush();
            return;
        }

        hook.Invoke(new SubModule(), new object[] { 0.1f });
        Equal(1, victim.RegisteredBlows, "same-tick hook registers one blow");
    }

    private static void CrossMissionBlowIsDroppedBeforeActivityRead()
    {
        Mission source = new Mission();
        Mission.Current = source;
        Agent victim = NewAgent(source, true);
        Enqueue(victim);
        Mission.Current = new Mission();
        victim.ThrowWhenActivityIsRead = true;

        DeferredElephantBlows.Flush();
        Equal(0, victim.ActivityReads, "cross-mission victim is not probed");
        Equal(0, victim.RegisteredBlows, "cross-mission blow is dropped");
    }

    private static void InactiveVictimIsDropped()
    {
        Mission mission = new Mission();
        Mission.Current = mission;
        Agent victim = NewAgent(mission, false);
        Enqueue(victim);

        DeferredElephantBlows.Flush();
        Equal(1, victim.ActivityReads, "inactive victim is checked once");
        Equal(0, victim.RegisteredBlows, "inactive victim receives no blow");
    }

    private static Agent NewAgent(Mission mission, bool active)
    {
        return new Agent { Mission = mission, Active = active };
    }

    private static void Enqueue(Agent victim)
    {
        Blow blow = new Blow { Marker = 17 };
        AttackCollisionData collision = new AttackCollisionData { Marker = 23 };
        DeferredElephantBlows.QueueRegisterBlow(victim, blow, ref collision);
    }

    private static void ConfigureLegacyReflectionTargetIfPresent()
    {
        FieldInfo field = typeof(DeferredElephantBlows).GetField(
            "OriginalRegisterBlow", BindingFlags.Static | BindingFlags.NonPublic);
        if (field != null)
            field.SetValue(null, typeof(Agent).GetMethod("RegisterBlow"));
    }

    private static void Equal(int expected, int actual, string name)
    {
        if (expected == actual) return;
        Fail(name + ": expected " + expected + ", got " + actual);
    }

    private static void Fail(string message)
    {
        _failures++;
        Console.Error.WriteLine("FAIL: " + message);
    }
}
```

- [ ] **Step 2: Run the harness and verify the regression is red**

Run:

```powershell
dotnet run --project tests/BehaviorHarness/BehaviorHarness.csproj --configuration Release
```

Expected: exit code 1, reporting the missing `AfterAsyncTickTick` override,
the cross-mission blow being registered, and the inactive victim receiving a
blow. The failure names the three production breaks the fix must prevent.

- [ ] **Step 3: Implement the minimum same-tick dispatcher**

In `SubModule`, add:

```csharp
protected override void AfterAsyncTickTick(float dt)
{
    DeferredElephantBlows.Flush();
}
```

Add `public Mission SourceMission;` to `PendingBlow`, capture
`pending.SourceMission = victim.Mission`, and replace `Flush` with:

```csharp
public static void Flush()
{
    Mission currentMission = Mission.Current;
    PendingBlow pending;

    while (Queue.TryDequeue(out pending))
    {
        if (currentMission == null ||
            !ReferenceEquals(pending.SourceMission, currentMission))
            continue;

        Agent victim = pending.Victim;
        if (victim == null ||
            !ReferenceEquals(victim.Mission, currentMission) ||
            !victim.IsActive())
            continue;

        victim.RegisterBlow(pending.Blow, in pending.CollisionData);
    }
}
```

Remove `OriginalRegisterBlow`, its transpiler assignment, all reflective
invocation code, and the entire `ElephantMissionMainThreadPatch` type. Keep the
strict signature and exactly-one-replacement validation unchanged.

- [ ] **Step 4: Run the focused and baseline suites green**

Run:

```powershell
dotnet run --project tests/BehaviorHarness/BehaviorHarness.csproj --configuration Release
python -m unittest discover -s tests -v
```

Expected: behavior harness `3/3`; Python suite `9` tests passing.

- [ ] **Step 5: Commit the reviewed behavior change**

```powershell
git add tests/BehaviorHarness src/RoTElephantThreadFix/RoTElephantThreadFix.cs
git commit -m "fix: flush elephant blows after the same agent tick"
```

### Task 2: Release metadata, CI, and user documentation

**Files:**
- Modify: `tests/test_package_module.py`
- Modify: `tests/test_repo_contract.py`
- Modify: `src/RoTElephantThreadFix/RoTElephantThreadFix.csproj`
- Modify: `module/SubModule.xml`
- Modify: `.github/workflows/build.yml`
- Modify: `README.md`

**Interfaces:**
- Consumes: Task 1's behavior harness and `AfterAsyncTickTick` implementation.
- Produces: v1.0.2 assembly metadata, install metadata, CI artifacts, and documented same-tick behavior.

- [ ] **Step 1: Change release contract tests to v1.0.2 first**

Update package test filenames to `RoTElephantThreadFix_v1.0.2.zip`. Rename the
module version test to `test_submodule_identity_and_version_are_v102` and expect
`v1.0.2`. Replace the old reflection/main-mission-tick contract with assertions
that require `AfterAsyncTickTick`, `SourceMission`, `victim.IsActive()`, and a
direct `victim.RegisterBlow`, while rejecting `ElephantMissionMainThreadPatch`
and `registerBlow.Invoke`. Require README/workflow artifact strings to use
`RoTElephantThreadFix_v1.0.2.zip` and `RoTElephantThreadFix-v1.0.2`.

- [ ] **Step 2: Run repository tests and verify release metadata is red**

Run:

```powershell
python -m unittest discover -s tests -v
```

Expected: failures identify the remaining `1.0.1` project, module, README, and
workflow values; behavior tests from Task 1 remain unaffected.

- [ ] **Step 3: Update all release surfaces and CI behavior test execution**

Set project `Version` to `1.0.2`, assembly/file versions to `1.0.2.0`, and
module version to `v1.0.2`. Change workflow ZIP path and artifact name to
v1.0.2, and add this step after the main DLL build:

```yaml
      - name: Test same-tick dispatch behavior
        run: dotnet run --project tests/BehaviorHarness/BehaviorHarness.csproj --configuration Release
```

Rewrite README compatibility, install artifact, GitHub Actions artifact, and
explanation sections to state that v1.0.2 drains at `AfterAsyncTickTick` in the
same agent-tick cycle, validates mission/agent lifetime, and calls
`RegisterBlow` directly.

- [ ] **Step 4: Run both suites green**

Run:

```powershell
dotnet run --project tests/BehaviorHarness/BehaviorHarness.csproj --configuration Release
python -m unittest discover -s tests -v
```

Expected: behavior harness `3/3`; Python suite `9` tests passing.

- [ ] **Step 5: Commit release metadata and documentation**

```powershell
git add .github README.md module src tests
git commit -m "chore: release RoTElephantThreadFix v1.0.2"
```

### Task 3: Release build and deterministic deliverables

**Files:**
- Create: `dist/RoTElephantThreadFix_v1.0.2.zip` (generated, not committed)
- Create: workspace `outputs/RoTElephantThreadFix_v1.0.2.zip` (delivered)
- Create: workspace `outputs/RoTElephantThreadFix_v1.0.2.dll` (delivered)
- Create: workspace `outputs/RoTElephantThreadFix-v1.0.2-source.zip` (delivered)

**Interfaces:**
- Consumes: Task 2's v1.0.2 source, module XML, and package filename.
- Produces: verified binary, install archive, and source archive.

- [ ] **Step 1: Restore and build only public dependencies**

```powershell
dotnet restore src/RoTElephantThreadFix/RoTElephantThreadFix.csproj
dotnet build src/RoTElephantThreadFix/RoTElephantThreadFix.csproj --configuration Release --no-restore
```

Expected: zero errors, zero warnings, and a non-empty
`src/RoTElephantThreadFix/bin/Release/net472/RoTElephantThreadFix.dll`.

- [ ] **Step 2: Re-run both suites against the final tree**

```powershell
dotnet run --project tests/BehaviorHarness/BehaviorHarness.csproj --configuration Release
python -m unittest discover -s tests -v
```

Expected: behavior harness `3/3`; Python suite `9` tests passing.

- [ ] **Step 3: Build and inspect the deterministic module package**

```powershell
python scripts/package_module.py --dll src/RoTElephantThreadFix/bin/Release/net472/RoTElephantThreadFix.dll --submodule module/SubModule.xml --output dist/RoTElephantThreadFix_v1.0.2.zip
```

Open the ZIP and assert the names are exactly:

```text
RoTElephantThreadFix/SubModule.xml
RoTElephantThreadFix/bin/Win64_Shipping_Client/RoTElephantThreadFix.dll
```

Verify the XML says `v1.0.2`, the DLL file/assembly version is `1.0.2.0`, and
the archive contains no `ROT.dll`, TaleWorlds assembly, or `0Harmony.dll`.

- [ ] **Step 4: Stage user-facing outputs without modifying the game install**

Copy the verified install ZIP and DLL into the task's `outputs` directory and
create a source ZIP from tracked files at `HEAD`, excluding `.git`, worktrees,
build outputs, SDD scratch, and NuGet caches. Record SHA-256 hashes for all
three deliverables.
