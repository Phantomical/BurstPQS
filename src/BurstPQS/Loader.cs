using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BurstPQS.Map;
using HarmonyLib;
using Smooth.Collections;
using UnityEngine;

namespace BurstPQS;

/// <summary>
/// Marks this type as being the canonical <see cref="BatchPQSMod"/> adapter
/// for the type given by <paramref name="pqsMod"/>.
/// </summary>
/// <param name="pqsMod"></param>
///
/// <remarks>
/// This attribute will be automatically detected provided that your mod DLL
/// has a <see cref="KSPAssemblyDependency"/> on BurstPQS.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class BatchPQSModAttribute(Type pqsMod) : Attribute
{
    public Type PQSMod { get; } = pqsMod;
}

[KSPAddon(KSPAddon.Startup.Instantly, once: true)]
internal class BurstLoader : MonoBehaviour
{
    static readonly List<Assembly> Assemblies = [typeof(BurstLoader).Assembly];

    void Awake()
    {
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

        BurstMapSO.RegisterMapSOFactoryFunc<MapSO>(CreateStockMapSO);
        BurstMapSO.RegisterMapSOFactoryFunc<CBTextureAtlasSO>(CreateStockMapSO);
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

    static BurstMapSO CreateStockMapSO(MapSO mapSO) =>
        BurstMapSO.Create(new StockBurstMapSO(mapSO));
}

// Apply patches in PSystemSpawn so that the static initializers on the noise
// patches don't break KSPBurst.
[KSPAddon(KSPAddon.Startup.PSystemSpawn, once: true)]
internal class BurstHarmonyPatcher : MonoBehaviour
{
    readonly Harmony harmony = new("BurstPQS");

    void Awake()
    {
        harmony.PatchAll();
    }
}
