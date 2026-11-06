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
        var task = CoroutineTask.Start(
            this,
            TextureExporter.ExportPlanet(body, options, projections)
        );

        yield return task;

        ReportFailure(body, task);
    }

    IEnumerator RunAll(
        List<CelestialBody> bodies,
        TextureExportOptions options,
        TextureProjection[] projections
    )
    {
        var tasks = new List<CoroutineTask>(bodies.Count);
        using var guard = new TextureExporter.ExportGuard();

        foreach (var body in bodies)
        {
            tasks.Add(
                CoroutineTask.Start(this, TextureExporter.ExportPlanet(body, options, projections))
            );
        }

        for (int i = 0; i < tasks.Count; i++)
        {
            yield return tasks[i];

            ReportFailure(bodies[i], tasks[i]);
        }
    }

    static void ReportFailure(CelestialBody body, CoroutineTask task)
    {
        if (task.Error is null)
            return;

        Debug.LogException(task.Error);
        ScreenMessages.PostScreenMessage(
            $"Failed to export {body.displayName.LocalizeRemoveGender()}. See KSP.log for details."
        );
    }
}
