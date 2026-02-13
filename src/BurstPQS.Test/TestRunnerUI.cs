using System.IO;
using System.Text;
using KSP.Testing;
using KSP.UI.Screens;
using UnityEngine;

namespace BurstPQS.Test;

[KSPAddon(KSPAddon.Startup.MainMenu, false)]
public class TestRunnerUI : MonoBehaviour
{
    ApplicationLauncherButton button;
    TestResults results;
    bool showWindow;
    Vector2 scroll;
    Rect windowRect = new Rect(100, 100, 500, 600);

    static Texture2D buttonTexture;

    void Start()
    {
        if (buttonTexture == null)
        {
            buttonTexture = new Texture2D(38, 38, TextureFormat.RGBA32, false);
            var pixels = new Color32[38 * 38];
            for (int i = 0; i < pixels.Length; i++)
                pixels[i] = new Color32(0, 200, 120, 255);
            buttonTexture.SetPixels32(pixels);
            buttonTexture.Apply(false, true);
        }

        GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);
        if (ApplicationLauncher.Ready)
            OnAppLauncherReady();
    }

    void OnDestroy()
    {
        GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);
        if (button != null)
            ApplicationLauncher.Instance.RemoveModApplication(button);
    }

    void OnAppLauncherReady()
    {
        if (button != null)
            return;

        button = ApplicationLauncher.Instance.AddModApplication(
            OnButtonTrue,
            OnButtonFalse,
            null,
            null,
            null,
            null,
            ApplicationLauncher.AppScenes.MAINMENU,
            buttonTexture
        );
    }

    void OnButtonTrue()
    {
        RunTests();
        showWindow = true;
    }

    int passCount;
    int failCount;

    void RunTests()
    {
        results = TestManager.RunTests();

        // Note: Stock TestManager.RunTests() has success/failed swapped,
        // so we compute the counts ourselves from the individual test states.
        passCount = 0;
        failCount = 0;
        foreach (var state in results.states)
        {
            if (state.Succeeded)
                passCount++;
            else
                failCount++;
        }

        var summary =
            $"[BurstPQS.Test] {passCount} passed, {failCount} failed ({results.states.Count} total)";
        Debug.Log(summary);

        foreach (var state in results.states)
        {
            if (state.Succeeded)
                continue;
            var name = state.Info?.Name ?? "(unnamed)";
            Debug.LogError(
                $"[BurstPQS.Test] FAIL: {name}\n  Reason: {state.Reason}\n  Details: {state.Details}"
            );
        }

        WriteLogFile();
    }

    void WriteLogFile()
    {
        var logPath = Path.Combine(KSPUtil.ApplicationRootPath, "Logs", "BurstPQS.Test.log");
        Directory.CreateDirectory(Path.GetDirectoryName(logPath));

        var sb = new StringBuilder();
        sb.AppendLine($"BurstPQS Test Results - {System.DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"{passCount} passed, {failCount} failed ({results.states.Count} total)");
        sb.AppendLine();

        foreach (var state in results.states)
        {
            var name = state.Info?.Name ?? "(unnamed)";
            sb.AppendLine($"[{(state.Succeeded ? "PASS" : "FAIL")}] {name}");
            if (!state.Succeeded)
            {
                if (!string.IsNullOrEmpty(state.Reason))
                    sb.AppendLine($"  Reason: {state.Reason}");
                if (!string.IsNullOrEmpty(state.Details))
                    sb.AppendLine($"  Details: {state.Details}");
            }
        }

        File.WriteAllText(logPath, sb.ToString());
        Debug.Log($"[BurstPQS.Test] Results written to {logPath}");
    }

    void OnButtonFalse()
    {
        showWindow = false;
    }

    void OnGUI()
    {
        if (!showWindow || results == null)
            return;

        GUI.skin = HighLogic.Skin;
        Styles.Init();
        windowRect = GUILayout.Window(
            GetInstanceID(),
            windowRect,
            DrawWindow,
            "BurstPQS Test Results"
        );
    }

    void DrawWindow(int id)
    {
        GUILayout.BeginHorizontal();
        GUILayout.Label(
            $"{passCount} passed, {failCount} failed",
            failCount > 0 ? Styles.failLabel : Styles.passLabel
        );
        GUILayout.FlexibleSpace();
        if (GUILayout.Button("Re-run"))
        {
            RunTests();
        }
        if (GUILayout.Button("Close"))
        {
            showWindow = false;
            button?.SetFalse(false);
        }
        GUILayout.EndHorizontal();

        GUILayout.Space(4);

        scroll = GUILayout.BeginScrollView(scroll);
        foreach (var state in results.states)
        {
            GUILayout.BeginHorizontal();
            GUILayout.Label(
                state.Succeeded ? "PASS" : "FAIL",
                state.Succeeded ? Styles.passLabel : Styles.failLabel,
                GUILayout.Width(40)
            );
            GUILayout.Label(state.Info?.Name ?? "(unnamed)");
            GUILayout.EndHorizontal();

            if (!state.Succeeded)
            {
                if (!string.IsNullOrEmpty(state.Reason))
                    GUILayout.Label("  Reason: " + state.Reason, Styles.detailLabel);
                if (!string.IsNullOrEmpty(state.Details))
                    GUILayout.Label("  Details: " + state.Details, Styles.detailLabel);
            }
        }
        GUILayout.EndScrollView();

        GUI.DragWindow();
    }

    static class Styles
    {
        static bool initialized;
        internal static GUIStyle passLabel;
        internal static GUIStyle failLabel;
        internal static GUIStyle detailLabel;

        internal static void Init()
        {
            if (initialized)
                return;
            initialized = true;

            passLabel = new GUIStyle(HighLogic.Skin.label) { normal = { textColor = Color.green } };
            failLabel = new GUIStyle(HighLogic.Skin.label) { normal = { textColor = Color.red } };
            detailLabel = new GUIStyle(HighLogic.Skin.label)
            {
                normal = { textColor = new Color(1f, 0.8f, 0.4f) },
                wordWrap = true,
                fontSize = 11,
            };
        }
    }
}
