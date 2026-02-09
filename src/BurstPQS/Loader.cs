using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Reflection.Emit;
using BurstPQS.Map;
using HarmonyLib;
using Smooth.Collections;
using Unity.Burst;
using UnityEngine;
using UnityEngine.UIElements;

namespace BurstPQS;

[AttributeUsage(AttributeTargets.Method, AllowMultiple = false, Inherited = false)]
public sealed class BurstPQSAutoPatchAttribute : Attribute { }

[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BatchPQSModAttribute(Type pqsMod) : Attribute
{
    public Type PQSMod { get; } = pqsMod;
}

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class BurstLoader : MonoBehaviour
{
    internal static readonly Harmony Harmony = new("BurstPQS");

    static readonly List<Assembly> Assemblies = [typeof(BurstLoader).Assembly];

    void Awake()
    {
        Harmony.PatchAll();

        Assemblies.AddAll(
            AssemblyLoader
                .loadedAssemblies.Where(assembly =>
                    assembly.deps.Any(dep => dep.name == "BurstPQS")
                )
                .Select(loadedAssembly => loadedAssembly.assembly)
        );
    }

    void Start()
    {
        RegisterBatchPQSMods();

        BurstMapSO.RegisterMapSOFactoryFunc<MapSO>(mapSO =>
            BurstMapSO.Create(new StockBurstMapSO(mapSO))
        );
    }

    #region BatchPQSMod Registration
    static void RegisterBatchPQSMods()
    {
        foreach (var assembly in Assemblies)
        {
            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<BatchPQSModAttribute>();
                if (attribute is null)
                    continue;

                if (attribute.PQSMod is null)
                    continue;

                try
                {
                    BatchPQSMod.RegisterBatchPQSMod(type, attribute.PQSMod);
                    Debug.Log(
                        $"[BurstPQS] Registered BatchPQSMod {type.Name} for PQSMod {attribute.PQSMod}"
                    );
                }
                catch (Exception e)
                {
                    Debug.LogError($"Failed to register BatchPQSMod {type.Name}");
                    Debug.LogException(e);
                }
            }
        }
    }
    #endregion
}
