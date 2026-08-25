using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;

namespace HarmonyLib
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
    public sealed class HarmonyPatch : Attribute { }

    public sealed class Harmony
    {
        private static readonly HashSet<string> ActiveOwnerIds =
            new HashSet<string>();

        private readonly string _id;

        public static int PatchAllCalls { get; private set; }
        public static int UnpatchAllCalls { get; private set; }
        public static string LastUnpatchId { get; private set; }
        public static bool ThrowAfterPatchBegins { get; set; }

        public Harmony(string id)
        {
            _id = id;
        }

        public static void ResetTracking()
        {
            PatchAllCalls = 0;
            UnpatchAllCalls = 0;
            LastUnpatchId = null;
        }

        public void PatchAll(Assembly assembly)
        {
            PatchAllCalls++;
            ActiveOwnerIds.Add(_id);

            if (ThrowAfterPatchBegins)
                throw new InvalidOperationException("Simulated partial patch failure");
        }

        public void UnpatchAll(string harmonyId = null)
        {
            UnpatchAllCalls++;
            LastUnpatchId = harmonyId;

            if (harmonyId == null)
                ActiveOwnerIds.Clear();
            else
                ActiveOwnerIds.Remove(harmonyId);
        }

        public static bool IsOwnerPatched(string harmonyId)
        {
            return ActiveOwnerIds.Contains(harmonyId);
        }
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

namespace TaleWorlds.Core
{
    public sealed class Game { }
}

namespace TaleWorlds.MountAndBlade
{
    public abstract class MBSubModuleBase
    {
        protected virtual void OnSubModuleLoad() { }
        public virtual void OnGameInitializationFinished(
            TaleWorlds.Core.Game game) { }
        public virtual void OnGameEnd(TaleWorlds.Core.Game game) { }
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
        private Mission _mission;

        public ManualResetEventSlim MissionReadEntered { get; set; }
        public ManualResetEventSlim AllowMissionRead { get; set; }

        public Mission Mission
        {
            get
            {
                if (MissionReadEntered != null)
                    MissionReadEntered.Set();

                if (AllowMissionRead != null &&
                    !AllowMissionRead.Wait(TimeSpan.FromSeconds(5)))
                {
                    throw new TimeoutException(
                        "Timed out waiting to release the Mission getter.");
                }

                return _mission;
            }
            set { _mission = value; }
        }
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
