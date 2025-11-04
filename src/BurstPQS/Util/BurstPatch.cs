using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using Unity.Burst;
using UnityEngine;

namespace BurstPQS.Util;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BurstPQSAutoPatchAttribute : Attribute { }

public static class BurstMethodPatcher
{
    internal static readonly Harmony Harmony = new("BurstPQS");

    static readonly List<Assembly> Assemblies = [typeof(BurstMethodPatcher).Assembly];
    static int TypeIndex = 0;

    static void PatchAll()
    {
        const BindingFlags Static =
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        var name = new AssemblyName("BurstPQSDynamic");
        var asmb = AppDomain.CurrentDomain.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        var modb = asmb.DefineDynamicModule("BurstPQSDynamic");

        foreach (var assembly in Assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                if (type.GetCustomAttribute<BurstCompileAttribute>() is null)
                    continue;

                foreach (var method in type.GetMethods(Static))
                {
                    if (method.GetCustomAttribute<BurstCompileAttribute>() is null)
                        return;
                    if (method.GetCustomAttribute<BurstPQSAutoPatchAttribute>() is null)
                        return;

                    PatchBurstMethod(method, modb);
                }
            }
        }
    }

    static void PatchBurstMethod(MethodInfo method, ModuleBuilder modb)
    {
        if (!method.IsStatic)
            return;
        if (!method.DeclaringType.IsClass)
        {
            Debug.LogWarning(
                $"[BurstPQS] Method {method.DeclaringType.FullName}.{method.Name} is defined on a struct. This will cause unity to crash so it is not being patched."
            );
            return;
        }

        var dtype = Expression.GetDelegateType(
            [.. method.GetParameters().Select(p => p.ParameterType), method.ReturnType]
        );
        var fptype = typeof(FunctionPointer<>).MakeGenericType(dtype);
        var fp = typeof(BurstCompiler)
            .GetMethod(nameof(BurstCompiler.CompileFunctionPointer))
            .MakeGenericMethod(dtype)
            .Invoke(null, [method.CreateDelegate(dtype)]);

        var invokep = fptype.GetProperty(nameof(FunctionPointer<>.Invoke));
        var createdp = fptype.GetProperty(nameof(FunctionPointer<>.IsCreated));

        if (!(bool)createdp.GetValue(fp))
        {
            Debug.LogWarning(
                $"[BurstPQS] A function pointer for {method.DeclaringType.FullName}.{method.Name} could not be created."
            );
            return;
        }

        var dg = fptype.GetProperty(nameof(FunctionPointer<>.Invoke));
        var tb = modb.DefineType(
            $"{method.DeclaringType.Name}_{method.Name}_{TypeIndex++}",
            TypeAttributes.Class
                | TypeAttributes.Sealed
                | TypeAttributes.Abstract
                | TypeAttributes.AnsiClass
                | TypeAttributes.Public
        );

        tb.DefineField("Fp", fptype, FieldAttributes.Static | FieldAttributes.Public);
        tb.DefineField("Del", dtype, FieldAttributes.Static | FieldAttributes.Public);

        var ty = tb.CreateType();
        ty.GetField("Fp").SetValue(null, fp);
        ty.GetField("Del").SetValue(null, dg);

        GeneratedMethodTypes.Add(method, ty);
        Harmony.Patch(
            method,
            transpiler: new HarmonyMethod(
                SymbolExtensions.GetMethodInfo(() => BurstTranspiler(null, null))
            )
        );
    }

    static readonly Dictionary<MethodBase, Type> GeneratedMethodTypes = [];

    delegate void TestDelegate(in int a, ref int b, double c);

    static IEnumerable<CodeInstruction> BurstTranspiler(
        IEnumerable<CodeInstruction> instructions,
        MethodBase method
    )
    {
        if (!GeneratedMethodTypes.TryGetValue(method, out var cacheTy))
            return instructions;
        if (!method.IsStatic)
            return instructions;

        var delegateField = cacheTy.GetField("Del");
        var delegateTy = delegateField.FieldType;
        List<CodeInstruction> output = [new(OpCodes.Ldsfld, delegateField)];

        int index = 0;
        foreach (var param in method.GetParameters())
        {
            var pty = param.ParameterType;
            if (pty.IsByRef)
                output.Add(new(OpCodes.Ldarga, index));
            else
                output.Add(new(OpCodes.Ldarg, index));

            index += 1;
        }

        output.Add(new(OpCodes.Callvirt, delegateTy.GetMethod("Invoke")));
        output.Add(new(OpCodes.Ret));

        return output;
    }
}
