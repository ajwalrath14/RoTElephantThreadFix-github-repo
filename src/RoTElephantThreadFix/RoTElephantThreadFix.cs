using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using TaleWorlds.MountAndBlade;

namespace RoTElephantThreadFix
{
    public sealed class SubModule : MBSubModuleBase
    {
        private static Harmony _harmony;

        protected override void OnSubModuleLoad()
        {
            base.OnSubModuleLoad();

            if (_harmony != null)
                return;

            _harmony = new Harmony("austin.rot.elephant.threadfix");
            _harmony.PatchAll(Assembly.GetExecutingAssembly());
        }
    }

    internal struct PendingBlow
    {
        public Agent Victim;
        public Blow Blow;
        public AttackCollisionData CollisionData;
    }

    internal static class DeferredElephantBlows
    {
        private static readonly ConcurrentQueue<PendingBlow> Queue =
            new ConcurrentQueue<PendingBlow>();

        // Set from the exact RegisterBlow call found in RoT's IL.
        internal static MethodInfo OriginalRegisterBlow;

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
            pending.Blow = blow;
            pending.CollisionData = collisionData;

            Queue.Enqueue(pending);
        }

        public static void Flush()
        {
            MethodInfo registerBlow = OriginalRegisterBlow;
            if (registerBlow == null)
                return;

            PendingBlow pending;

            while (Queue.TryDequeue(out pending))
            {
                Agent victim = pending.Victim;
                if (victim == null)
                    continue;

                object[] args = new object[]
                {
                    pending.Blow,
                    pending.CollisionData
                };

                try
                {
                    registerBlow.Invoke(victim, args);
                }
                catch (TargetInvocationException tie)
                {
                    // Do not hide a genuine Bannerlord/RoT failure.
                    if (tie.InnerException != null)
                        throw tie.InnerException;

                    throw;
                }
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

                    DeferredElephantBlows.OriginalRegisterBlow = called;

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

    [HarmonyPatch]
    internal static class ElephantMissionMainThreadPatch
    {
        private static MethodBase TargetMethod()
        {
            Type type = AccessTools.TypeByName(
                "RoT_Elephants.RoTElephantMissionLogic");

            if (type == null)
                throw new TypeLoadException(
                    "RoT_Elephants.RoTElephantMissionLogic was not found.");

            MethodInfo found = null;

            foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
            {
                if (method.Name == "OnMissionTick")
                {
                    if (found != null)
                        throw new AmbiguousMatchException(
                            "More than one OnMissionTick method exists on " +
                            type.FullName + ".");

                    found = method;
                }
            }

            if (found == null)
                throw new MissingMethodException(
                    type.FullName,
                    "OnMissionTick");

            return found;
        }

        private static void Postfix()
        {
            // Runs from RoT's MissionLogic tick rather than Agent.TickParallel.
            DeferredElephantBlows.Flush();
        }
    }
}
