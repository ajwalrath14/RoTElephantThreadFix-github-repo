using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.Core;
using TaleWorlds.MountAndBlade;

namespace RoTElephantThreadFix
{
    public sealed class SubModule : MBSubModuleBase
    {
        private const string HarmonyId = "austin.rot.elephant.threadfix";
        private static Harmony _harmony;

        public override void OnGameInitializationFinished(Game game)
        {
            base.OnGameInitializationFinished(game);

            if (_harmony != null)
                return;

            Harmony harmony = new Harmony(HarmonyId);

            try
            {
                harmony.PatchAll(Assembly.GetExecutingAssembly());
                DeferredElephantBlows.StartAccepting();
                _harmony = harmony;
            }
            catch
            {
                DeferredElephantBlows.StopAcceptingAndClear();
                harmony.UnpatchAll(HarmonyId);
                throw;
            }
        }

        public override void OnGameEnd(Game game)
        {
            try
            {
                DeferredElephantBlows.StopAcceptingAndClear();

                if (_harmony != null)
                    _harmony.UnpatchAll(HarmonyId);
            }
            finally
            {
                _harmony = null;
                base.OnGameEnd(game);
            }
        }

        protected override void AfterAsyncTickTick(float dt)
        {
            DeferredElephantBlows.Flush();
        }
    }

    internal struct PendingBlow
    {
        public Agent Victim;
        public Mission SourceMission;
        public Blow Blow;
        public AttackCollisionData CollisionData;
    }

    internal static class DeferredElephantBlows
    {
        private static readonly object LifecycleSync = new object();
        private static readonly Queue<PendingBlow> Queue =
            new Queue<PendingBlow>();
        private static bool _accepting;

        public static void StartAccepting()
        {
            lock (LifecycleSync)
            {
                Queue.Clear();
                _accepting = true;
            }
        }

        public static void StopAcceptingAndClear()
        {
            lock (LifecycleSync)
            {
                _accepting = false;
                Queue.Clear();
            }
        }

        // This static method deliberately has the same evaluation-stack shape
        // as: victim.RegisterBlow(blow, ref/in collisionData)
        public static void QueueRegisterBlow(
            Agent victim,
            Blow blow,
            ref AttackCollisionData collisionData)
        {
            if (victim == null)
                return;

            PendingBlow pending = new PendingBlow();
            pending.Victim = victim;
            pending.SourceMission = victim.Mission;
            pending.Blow = blow;
            pending.CollisionData = collisionData;

            lock (LifecycleSync)
            {
                if (!_accepting)
                    return;

                Queue.Enqueue(pending);
            }
        }

        public static void Flush()
        {
            PendingBlow[] pendingBlows;

            lock (LifecycleSync)
            {
                if (!_accepting)
                {
                    Queue.Clear();
                    return;
                }

                if (Queue.Count == 0)
                    return;

                pendingBlows = Queue.ToArray();
                Queue.Clear();
            }

            // Bannerlord serializes this post-agent flush with OnGameEnd on the
            // game thread. Keep engine callbacks outside LifecycleSync so they
            // cannot invert this queue's lock through mission behavior code.
            Mission currentMission = Mission.Current;

            foreach (PendingBlow pending in pendingBlows)
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
    }

    [HarmonyPatch]
    internal static class ElephantParallelAttackPatch
    {
        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(
                "RoT_Elephants.RoTElephantAgentComponent");

            if (type == null)
                throw new TypeLoadException(
                    "RoT_Elephants.RoTElephantAgentComponent was not found.");

            MethodInfo found = null;

            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name == "OnTickParallel")
                {
                    if (found != null)
                        throw new AmbiguousMatchException(
                            "More than one OnTickParallel method exists on " +
                            type.FullName + ".");

                    found = method;
                }
            }

            if (found == null)
                throw new MissingMethodException(
                    type.FullName,
                    "OnTickParallel");

            return found;
        }

        private static IEnumerable<CodeInstruction> Transpiler(
            IEnumerable<CodeInstruction> instructions)
        {
            MethodInfo replacement = AccessTools.Method(
                typeof(DeferredElephantBlows),
                "QueueRegisterBlow");

            if (replacement == null)
                throw new MissingMethodException(
                    "DeferredElephantBlows.QueueRegisterBlow could not be resolved.");

            int replacements = 0;

            foreach (CodeInstruction instruction in instructions)
            {
                MethodInfo called = instruction.operand as MethodInfo;

                if ((instruction.opcode == OpCodes.Call ||
                     instruction.opcode == OpCodes.Callvirt) &&
                    called != null &&
                    called.DeclaringType == typeof(Agent) &&
                    called.Name == "RegisterBlow")
                {
                    ParameterInfo[] parameters = called.GetParameters();

                    bool expectedShape =
                        parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(Blow) &&
                        parameters[1].ParameterType ==
                            typeof(AttackCollisionData).MakeByRefType();

                    if (!expectedShape)
                        throw new InvalidOperationException(
                            "Found Agent.RegisterBlow in RoT elephant code, but its " +
                            "signature does not match the crash-dump version.");

                    CodeInstruction patched =
                        new CodeInstruction(OpCodes.Call, replacement);

                    patched.labels.AddRange(instruction.labels);
                    patched.blocks.AddRange(instruction.blocks);

                    replacements++;
                    yield return patched;
                    continue;
                }

                yield return instruction;
            }

            if (replacements != 1)
            {
                throw new InvalidOperationException(
                    "RoTElephantThreadFix expected exactly one Agent.RegisterBlow " +
                    "call in RoTElephantAgentComponent.OnTickParallel, but found " +
                    replacements + ".");
            }
        }
    }

}
