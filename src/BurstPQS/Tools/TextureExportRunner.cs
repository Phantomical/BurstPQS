using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BurstPQS.Tools;

/// <summary>
/// Hosts texture export coroutines on a persistent GameObject so an export is not tied to the
/// lifetime of the UI that started it.
/// </summary>
/// <remarks>
/// Deactivating a GameObject stops its coroutines without resuming them, so the <c>using</c>
/// blocks inside an export never dispose: <see cref="TextureExporter.IsExporting"/> stays
/// true, the exporter's NativeArrays leak, and a PQS can be left in map-building mode.
/// </remarks>
internal class TextureExportRunner : MonoBehaviour
{
    static TextureExportRunner _instance;

    static TextureExportRunner Instance
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
    /// Starts exporting a single body. Does nothing if an export is already running.
    /// </summary>
    internal static void ExportPlanet(CelestialBody body, TextureExportOptions options)
    {
        if (TextureExporter.IsExporting)
            return;

        Instance.StartCoroutine(Instance.RunOne(body, options));
    }

    /// <summary>
    /// Starts exporting every body in <paramref name="bodies"/>. Does nothing if an export is
    /// already running.
    /// </summary>
    internal static void ExportPlanets(
        IEnumerable<CelestialBody> bodies,
        TextureExportOptions options
    )
    {
        if (TextureExporter.IsExporting)
            return;

        // Copied: callers may rebuild their list while the export runs.
        Instance.StartCoroutine(Instance.RunAll([.. bodies], options));
    }

    IEnumerator RunOne(CelestialBody body, TextureExportOptions options)
    {
        using var guard = new TextureExporter.ExportGuard();

        yield return TextureExporter.ExportPlanet(body, options);
    }

    IEnumerator RunAll(List<CelestialBody> bodies, TextureExportOptions options)
    {
        var coroutines = new Queue<Coroutine>();
        using var guard = new TextureExporter.ExportGuard();

        foreach (var body in bodies)
            coroutines.Enqueue(StartCoroutine(TextureExporter.ExportPlanet(body, options)));

        foreach (var coroutine in coroutines)
            yield return coroutine;
    }
}
