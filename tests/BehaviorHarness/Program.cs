using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using RoTElephantThreadFix;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

internal static class Program
{
    private static int failures;

    private static int Main()
    {
        SetLegacyRegisterBlowWhenPresent();

        DefersHarmonyPatchUntilGameInitializationFinishes();
        CleansUpAfterPartialPatchFailure();
        UnpatchesAndDropsQueuedBlowsAtGameEnd();
        RejectsInFlightEnqueueWithoutBlockingGameEnd();

        SubModule queueTestSubModule = StartGame();
        try
        {
            FlushesQueuedBlowDuringSubModuleAgentTick();
            DoesNotReadActivityOrReplayAcrossMissions();
            ReadsActivityAndSkipsInactiveVictim();
        }
        finally
        {
            queueTestSubModule.OnGameEnd(new Game());
        }

        if (failures != 0)
            return 1;

        Console.WriteLine("Behavior harness passed: 7/7");
        return 0;
    }

    private static void DefersHarmonyPatchUntilGameInitializationFinishes()
    {
        Harmony.ResetTracking();
        SubModule subModule = new SubModule();

        MethodInfo subModuleLoad = typeof(SubModule).GetMethod(
            "OnSubModuleLoad",
            BindingFlags.Instance | BindingFlags.NonPublic);

        if (subModuleLoad == null)
        {
            Console.WriteLine("FAIL: OnSubModuleLoad override is missing");
            failures++;
        }
        else
        {
            subModuleLoad.Invoke(subModule, null);
        }

        AssertEqual("Harmony is not patched during module loading", 0,
            Harmony.PatchAllCalls);

        Mission preInitializationMission = new Mission();
        Mission.Current = preInitializationMission;
        Agent preInitializationVictim = new Agent
        {
            Mission = preInitializationMission,
            Active = true
        };
        Queue(preInitializationVictim);
        DeferredElephantBlows.Flush();
        AssertEqual("blows are rejected before game initialization", 0,
            preInitializationVictim.RegisteredBlows);
        Mission.Current = null;

        MethodInfo gameInitializationFinished = typeof(SubModule).GetMethod(
            "OnGameInitializationFinished",
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.DeclaredOnly);

        if (gameInitializationFinished == null)
        {
            Console.WriteLine(
                "FAIL: OnGameInitializationFinished override is missing");
            failures++;
        }
        else
        {
            Game game = new Game();
            gameInitializationFinished.Invoke(subModule, new object[] { game });
            gameInitializationFinished.Invoke(subModule, new object[] { game });
        }

        AssertEqual("Harmony is patched once after game initialization", 1,
            Harmony.PatchAllCalls);

        MethodInfo gameEnd = typeof(SubModule).GetMethod(
            "OnGameEnd",
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.DeclaredOnly);
        if (gameEnd != null && gameInitializationFinished != null)
        {
            gameEnd.Invoke(subModule, new object[] { new Game() });

            Harmony.ResetTracking();
            gameInitializationFinished.Invoke(
                subModule,
                new object[] { new Game() });
            AssertEqual("Harmony is patched again for the next game", 1,
                Harmony.PatchAllCalls);
            gameEnd.Invoke(subModule, new object[] { new Game() });
        }
    }

    private static void CleansUpAfterPartialPatchFailure()
    {
        const string ownerId = "austin.rot.elephant.threadfix";
        Harmony.ResetTracking();
        Harmony.ThrowAfterPatchBegins = true;
        SubModule subModule = new SubModule();
        bool sawExpectedFailure = false;

        try
        {
            subModule.OnGameInitializationFinished(new Game());
        }
        catch (InvalidOperationException)
        {
            sawExpectedFailure = true;
        }
        finally
        {
            Harmony.ThrowAfterPatchBegins = false;
        }

        AssertEqual("partial patch failure is propagated", 1,
            sawExpectedFailure ? 1 : 0);
        AssertEqual("partial patch failure is immediately unpatched", 1,
            Harmony.UnpatchAllCalls);
        AssertEqual("partial patch cleanup uses this mod's Harmony ID",
            ownerId, Harmony.LastUnpatchId);
        AssertEqual("partial patch leaves no active owner", 0,
            Harmony.IsOwnerPatched(ownerId) ? 1 : 0);

        Mission mission = new Mission();
        Mission.Current = mission;
        Agent victim = new Agent { Mission = mission, Active = true };
        Queue(victim);
        DeferredElephantBlows.Flush();
        AssertEqual("failed initialization leaves the queue disabled", 0,
            victim.RegisteredBlows);

        Harmony.ResetTracking();
        subModule.OnGameInitializationFinished(new Game());
        AssertEqual("initialization can retry after a patch failure", 1,
            Harmony.PatchAllCalls);
        subModule.OnGameEnd(new Game());
        Mission.Current = null;
    }

    private static void UnpatchesAndDropsQueuedBlowsAtGameEnd()
    {
        const string ownerId = "austin.rot.elephant.threadfix";
        const string foreignOwnerId = "test.foreign.owner";
        Harmony.ResetTracking();
        Harmony foreignHarmony = new Harmony(foreignOwnerId);
        foreignHarmony.PatchAll(Assembly.GetExecutingAssembly());
        SubModule subModule = new SubModule();
        MethodInfo gameInitializationFinished = typeof(SubModule).GetMethod(
            "OnGameInitializationFinished",
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.DeclaredOnly);

        if (gameInitializationFinished == null)
        {
            Console.WriteLine(
                "FAIL: cannot install Harmony before testing game-end cleanup");
            failures++;
        }
        else
        {
            gameInitializationFinished.Invoke(
                subModule,
                new object[] { new Game() });
        }

        Mission mission = new Mission();
        Mission.Current = mission;
        Agent victim = new Agent { Mission = mission, Active = true };
        Queue(victim);
        Harmony.ResetTracking();

        MethodInfo gameEnd = typeof(SubModule).GetMethod(
            "OnGameEnd",
            BindingFlags.Instance | BindingFlags.Public |
            BindingFlags.DeclaredOnly);

        if (gameEnd == null)
        {
            Console.WriteLine("FAIL: OnGameEnd override is missing");
            failures++;
        }
        else
        {
            gameEnd.Invoke(subModule, new object[] { new Game() });
        }

        DeferredElephantBlows.Flush();

        AssertEqual("game end removes this mod's Harmony patches", 1,
            Harmony.UnpatchAllCalls);
        AssertEqual("game end unpatches only this mod's Harmony ID",
            ownerId, Harmony.LastUnpatchId);
        AssertEqual("game end removes this mod's owner", 0,
            Harmony.IsOwnerPatched(ownerId) ? 1 : 0);
        AssertEqual("game end preserves foreign Harmony owners", 1,
            Harmony.IsOwnerPatched(foreignOwnerId) ? 1 : 0);
        AssertEqual("game end drops queued blows", 0, victim.RegisteredBlows);
        foreignHarmony.UnpatchAll(foreignOwnerId);
        Mission.Current = null;
    }

    private static void RejectsInFlightEnqueueWithoutBlockingGameEnd()
    {
        SubModule subModule = StartGame();
        Mission mission = new Mission();
        Mission.Current = mission;
        ManualResetEventSlim missionReadEntered = new ManualResetEventSlim(false);
        ManualResetEventSlim allowMissionRead = new ManualResetEventSlim(false);
        Agent victim = new Agent
        {
            Mission = mission,
            Active = true,
            MissionReadEntered = missionReadEntered,
            AllowMissionRead = allowMissionRead
        };

        Task enqueueTask = Task.Run(() => Queue(victim));
        bool enqueueReachedMissionRead = missionReadEntered.Wait(
            TimeSpan.FromSeconds(2));
        AssertEqual("controlled enqueue reaches the synchronized region", 1,
            enqueueReachedMissionRead ? 1 : 0);

        Task gameEndTask = Task.Run(
            () => subModule.OnGameEnd(new Game()));
        bool gameEndCompletedWhileGetterWasPaused = gameEndTask.Wait(
            TimeSpan.FromSeconds(2));
        AssertEqual("game end does not wait on an agent getter", 1,
            gameEndCompletedWhileGetterWasPaused ? 1 : 0);

        allowMissionRead.Set();
        bool tasksCompleted = Task.WaitAll(
            new[] { enqueueTask, gameEndTask },
            TimeSpan.FromSeconds(2));
        AssertEqual("enqueue and game-end tasks complete", 1,
            tasksCompleted ? 1 : 0);

        victim.MissionReadEntered = null;
        victim.AllowMissionRead = null;
        DeferredElephantBlows.Flush();
        AssertEqual("game end clears the completed in-flight enqueue", 0,
            victim.RegisteredBlows);

        Queue(victim);
        DeferredElephantBlows.Flush();
        AssertEqual("post-game-end enqueues are rejected", 0,
            victim.RegisteredBlows);

        subModule.OnGameInitializationFinished(new Game());
        Queue(victim);
        DeferredElephantBlows.Flush();
        AssertEqual("the queue is enabled for the next game", 1,
            victim.RegisteredBlows);
        subModule.OnGameEnd(new Game());
        Mission.Current = null;
    }

    private static SubModule StartGame()
    {
        SubModule subModule = new SubModule();
        subModule.OnGameInitializationFinished(new Game());
        return subModule;
    }

    private static void SetLegacyRegisterBlowWhenPresent()
    {
        FieldInfo originalRegisterBlow = typeof(DeferredElephantBlows).GetField(
            "OriginalRegisterBlow",
            BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);

        if (originalRegisterBlow != null)
        {
            originalRegisterBlow.SetValue(
                null,
                typeof(Agent).GetMethod(
                    "RegisterBlow",
                    BindingFlags.Instance | BindingFlags.Public));
        }
    }

    private static void FlushesQueuedBlowDuringSubModuleAgentTick()
    {
        Mission mission = new Mission();
        Mission.Current = mission;
        Agent victim = new Agent { Mission = mission, Active = true };
        Queue(victim);

        MethodInfo afterAsyncTickTick = typeof(SubModule).GetMethod(
            "AfterAsyncTickTick",
            BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        if (afterAsyncTickTick == null)
        {
            Console.WriteLine("FAIL: AfterAsyncTickTick override is missing");
            failures++;
            DeferredElephantBlows.Flush();
        }
        else
        {
            afterAsyncTickTick.Invoke(new SubModule(), new object[] { 0f });
        }

        AssertEqual("active victim queued in current mission is registered", 1,
            victim.RegisteredBlows);
    }

    private static void DoesNotReadActivityOrReplayAcrossMissions()
    {
        Mission missionA = new Mission();
        Mission missionB = new Mission();
        Mission.Current = missionA;
        Agent victim = new Agent { Mission = missionA, Active = true };
        Queue(victim);

        Mission.Current = missionB;
        victim.ThrowWhenActivityIsRead = true;
        DeferredElephantBlows.Flush();

        AssertEqual("stale mission blow does not read victim activity", 0,
            victim.ActivityReads);
        AssertEqual("stale mission blow is not registered", 0,
            victim.RegisteredBlows);
    }

    private static void ReadsActivityAndSkipsInactiveVictim()
    {
        Mission mission = new Mission();
        Mission.Current = mission;
        Agent victim = new Agent { Mission = mission, Active = false };
        Queue(victim);

        DeferredElephantBlows.Flush();

        AssertEqual("inactive victim activity is read", 1, victim.ActivityReads);
        AssertEqual("inactive victim blow is not registered", 0,
            victim.RegisteredBlows);
    }

    private static void Queue(Agent victim)
    {
        Blow blow = new Blow { Marker = 17 };
        AttackCollisionData collisionData = new AttackCollisionData { Marker = 23 };
        DeferredElephantBlows.QueueRegisterBlow(victim, blow, ref collisionData);
    }

    private static void AssertEqual(string behavior, int expected, int actual)
    {
        if (expected == actual)
            return;

        Console.WriteLine(
            "FAIL: " + behavior + " (expected " + expected + ", got " + actual + ")");
        failures++;
    }

    private static void AssertEqual(
        string behavior,
        string expected,
        string actual)
    {
        if (expected == actual)
            return;

        Console.WriteLine(
            "FAIL: " + behavior + " (expected " + expected + ", got " +
            actual + ")");
        failures++;
    }
}
