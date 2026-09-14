using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BurstPQS.Tools;

/// <summary>
/// Host object for texture export coroutines.
/// </summary>
internal class TextureExportRunner : MonoBehaviour
{
    static TextureExportRunner _instance;

    internal static TextureExportRunner Instance
    {
        get
        {
            if (_instance == null)
            {
                var go = new GameObject("BurstPQS_TextureExportRunner");
                DontDestroyOnLoad(go);
                _instance = go.AddComponent<TextureExportRunner>();
            }

            return _instance;
        }
    }

    /// <summary>
    /// Starts exporting a single body, once per entry in <paramref name="projections"/>. Does
    /// nothing if an export is already running.
    /// </summary>
    internal static void ExportPlanet(
        CelestialBody body,
        TextureExportOptions options,
        TextureProjection[] projections
    )
    {
        if (TextureExporter.IsExporting)
            return;

        Instance.StartCoroutine(Instance.RunOne(body, options, projections));
    }

    /// <summary>
    /// Starts exporting every body in <paramref name="bodies"/>, once per entry in
    /// <paramref name="projections"/>. Does nothing if an export is already running.
    /// </summary>
    internal static void ExportPlanets(
        IEnumerable<CelestialBody> bodies,
        TextureExportOptions options,
        TextureProjection[] projections
    )
    {
        if (TextureExporter.IsExporting)
            return;

        // Copied: callers may rebuild their list while the export runs.
        Instance.StartCoroutine(Instance.RunAll([.. bodies], options, projections));
    }

    IEnumerator RunOne(
        CelestialBody body,
        TextureExportOptions options,
        TextureProjection[] projections
    )
    {
        using var guard = new TextureExporter.ExportGuard();
        var coroutine = StartCoroutine(TextureExporter.ExportPlanet(body, options, projections));

        yield return coroutine;
    }

    IEnumerator RunAll(
        List<CelestialBody> bodies,
        TextureExportOptions options,
        TextureProjection[] projections
    )
    {
        var coroutines = new Queue<Coroutine>();
        using var guard = new TextureExporter.ExportGuard();

        foreach (var body in bodies)
        {
            coroutines.Enqueue(
                StartCoroutine(TextureExporter.ExportPlanet(body, options, projections))
            );
        }

        foreach (var coroutine in coroutines)
            yield return coroutine;
    }
}
