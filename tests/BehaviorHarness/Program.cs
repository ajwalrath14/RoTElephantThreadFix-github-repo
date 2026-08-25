using System;
using System.Reflection;
using RoTElephantThreadFix;
using TaleWorlds.MountAndBlade;

internal static class Program
{
    private static int failures;

    private static int Main()
    {
        SetLegacyRegisterBlowWhenPresent();

        FlushesQueuedBlowDuringSubModuleAgentTick();
        DoesNotReadActivityOrReplayAcrossMissions();
        ReadsActivityAndSkipsInactiveVictim();

        if (failures != 0)
            return 1;

        Console.WriteLine("Behavior harness passed: 3/3");
        return 0;
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
}
