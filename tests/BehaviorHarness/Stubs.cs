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
