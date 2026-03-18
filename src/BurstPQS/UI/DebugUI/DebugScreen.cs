using System.Text;
using BurstPQS.UI.Components;
using KSP.UI.Screens.DebugToolbar;
using KSP.UI.Screens.DebugToolbar.Screens;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.DebugUI;

[KSPAddon(KSPAddon.Startup.MainMenu, once: true)]
internal class BurstDebugScreenRegistrar : MonoBehaviour
{
    void Start()
    {
        if (!DebugUIManager.Initialize())
            return;

        var prefab = BurstDebugScreenContent.CreatePrefab();

        // Add to debugScreens.screens instead of calling DebugScreen.AddContentScreen directly.
        // AddDebugScreens.Start() processes this list into DebugScreen.treeItems after all
        // KSPAddon Start() methods have run, so this ends up after the stock screens in the tree.
        DebugScreenSpawner.Instance.debugScreens.screens.Add(
            new BurstScreenWrapper
            {
                parentName = null,
                name = "BurstPQS",
                text = "BurstPQS",
                screen = prefab,
            }
        );
    }

    class BurstScreenWrapper : AddDebugScreens.ScreenWrapper
    {
        public override string ToString() => name;
    }
}

internal class BurstDebugScreenContent : MonoBehaviour
{
    [SerializeField]
    private TableLayoutGroup _planetTable;

    internal static RectTransform CreatePrefab()
    {
        var rt = DebugUIManager.CreateScreenPrefab<BurstDebugScreenContent>("BurstPQS_DebugScreen");
        var content = rt.GetComponent<BurstDebugScreenContent>();
        content.BuildSettingsSection(rt);
        content.BuildPlanetsSection(rt);
        return rt;
    }

    private void BuildSettingsSection(Transform parent)
    {
        DebugUIManager.CreateHeader(parent, "Settings");
        CreateSeparator(parent);

        var toggleRow = DebugUIManager.CreateHorizontalLayout(parent);
        toggleRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        DebugUIManager.CreateToggle<ForceFallbackToggle>(
            toggleRow.transform,
            "Force Fallback Mode"
        );
        DebugUIManager.CreateHelpButton(
            toggleRow.transform,
            "Force all bodies to use the stock PQS terrain builder instead of "
                + "Burst-compiled jobs. Use this to diagnose terrain rendering issues."
        );

        var btnRow = DebugUIManager.CreateHorizontalLayout(parent);
        DebugUIManager.CreateButton<DumpLayoutButton>(btnRow.transform, "Dump Layout");
    }

    private void BuildPlanetsSection(Transform parent)
    {
        DebugUIManager.CreateSpacer(parent);

        var headerRow = DebugUIManager.CreateHorizontalLayout(parent);
        headerRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        DebugUIManager.CreateHeader(headerRow.transform, "Planets");
        DebugUIManager.CreateHelpButton(
            headerRow.transform,
            "Shows each body's current terrain build mode.\n"
                + "Burst — terrain is built using Burst-compiled jobs.\n"
                + "Fallback — terrain is built using the stock PQS method.\n"
                + "(either forced via the toggle above, or because Burst is unavailable)"
        );

        CreateSeparator(parent);

        // ScrollRect — fixed height, scrolls the planet list
        var scrollGo = new GameObject("PlanetsScroll", typeof(RectTransform));
        scrollGo.transform.SetParent(parent, false);
        var scrollLE = scrollGo.AddComponent<LayoutElement>();
        scrollLE.preferredHeight = 220f;
        scrollLE.flexibleHeight = 1f;
        scrollLE.flexibleWidth = 1f;

        var scrollRect = scrollGo.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(scrollGo.transform, false);
        var vp = viewportGo.GetComponent<RectTransform>();
        vp.anchorMin = Vector2.zero;
        vp.anchorMax = Vector2.one;
        vp.pivot = new Vector2(0, 0.5f);
        vp.offsetMin = Vector2.zero;
        vp.offsetMax = Vector2.zero;
        viewportGo.AddComponent<Image>();
        viewportGo.AddComponent<Mask>().showMaskGraphic = false;
        scrollRect.viewport = vp;

        var contentGo = new GameObject("Content", typeof(RectTransform));
        contentGo.transform.SetParent(viewportGo.transform, false);
        var cr = contentGo.GetComponent<RectTransform>();
        cr.anchorMin = new Vector2(0, 1);
        cr.anchorMax = new Vector2(1, 1);
        cr.pivot = new Vector2(0.5f, 1f);
        cr.offsetMin = Vector2.zero;
        cr.offsetMax = Vector2.zero;
        var tlg = contentGo.AddComponent<TableLayoutGroup>();
        tlg.MinimumColumnWidth = 0f;
        tlg.ColumnSpacing = 8f;
        tlg.RowSpacing = 2f;
        var ccsf = contentGo.AddComponent<ContentSizeFitter>();
        ccsf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = cr;

        DebugUIManager.CreateScrollbar(scrollGo.transform, scrollRect);

        _planetTable = tlg;
    }

    void OnEnable() => PopulatePlanetTable();

    void OnDisable() => ClearPlanetTable();

    internal void RefreshPlanetTable()
    {
        ClearPlanetTable();
        PopulatePlanetTable();
    }

    private void PopulatePlanetTable()
    {
        if (FlightGlobals.Bodies == null)
            return;

        var bodies = new System.Collections.Generic.List<(string name, bool fallback)>();
        foreach (var body in FlightGlobals.Bodies)
        {
            if (body.pqsController == null)
                continue;
            var batchPQS = body.pqsController.GetComponent<BatchPQS>();
            bodies.Add(
                (body.bodyDisplayName.Replace("^N", ""), batchPQS == null || batchPQS.Fallback)
            );
        }

        // Set row heights before adding children so TableLayoutGroup knows the row count.
        var heights = new float[bodies.Count];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = 20f;
        _planetTable.RowHeights = heights;

        foreach (var (name, fallback) in bodies)
        {
            DebugUIManager.CreateDirectLabel(_planetTable.transform, name);
            var statusTmp = DebugUIManager.CreateDirectLabel(
                _planetTable.transform,
                fallback ? "Fallback" : "Burst"
            );
            statusTmp.color = fallback
                ? new Color(1.0f, 0.35f, 0.35f)
                : new Color(0.35f, 1.0f, 0.35f);
        }
    }

    private void ClearPlanetTable()
    {
        for (int i = _planetTable.transform.childCount - 1; i >= 0; i--)
            Destroy(_planetTable.transform.GetChild(i).gameObject);
        _planetTable.RowHeights = [0f];
    }

    private static void CreateSeparator(Transform parent)
    {
        var go = new GameObject("Separator", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.4f, 0.4f, 0.4f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;
        le.flexibleWidth = 1f;
    }

    internal void DumpLiveLayout()
    {
        var sb = new StringBuilder();

        sb.AppendLine("BurstPQS_DebugScreen layout:");
        DebugDumpHelper.DumpGameObjectLayout(sb, gameObject, 0);

        sb.AppendLine();
        sb.AppendLine("Parent chain:");
        var t = transform.parent;
        int i = 0;
        while (t != null && i < 10)
        {
            var rt = t as RectTransform;
            sb.Append($"  [{i}] \"{t.name}\"");
            if (rt != null)
                sb.Append(
                    $" rect={rt.rect} anchorMin={rt.anchorMin} anchorMax={rt.anchorMax} offsetMin={rt.offsetMin} offsetMax={rt.offsetMax} pivot={rt.pivot}"
                );
            var le = t.GetComponent<LayoutElement>();
            if (le != null)
                sb.Append(
                    $" LE(minW={le.minWidth} minH={le.minHeight} prefW={le.preferredWidth} prefH={le.preferredHeight} flexW={le.flexibleWidth} flexH={le.flexibleHeight})"
                );
            var vlg = t.GetComponent<VerticalLayoutGroup>();
            if (vlg != null)
                sb.Append(
                    $" VLG(ctrlW={vlg.childControlWidth} ctrlH={vlg.childControlHeight} expandW={vlg.childForceExpandWidth} expandH={vlg.childForceExpandHeight} spacing={vlg.spacing} pad={vlg.padding})"
                );
            var hlg = t.GetComponent<HorizontalLayoutGroup>();
            if (hlg != null)
                sb.Append(
                    $" HLG(ctrlW={hlg.childControlWidth} ctrlH={hlg.childControlHeight} expandW={hlg.childForceExpandWidth} expandH={hlg.childForceExpandHeight} spacing={hlg.spacing} pad={hlg.padding})"
                );
            var csf = t.GetComponent<ContentSizeFitter>();
            if (csf != null)
                sb.Append($" CSF(horiz={csf.horizontalFit} vert={csf.verticalFit})");
            var scrollRect = t.GetComponent<ScrollRect>();
            if (scrollRect != null)
                sb.Append($" ScrollRect(horiz={scrollRect.horizontal} vert={scrollRect.vertical})");
            sb.AppendLine();
            t = t.parent;
            i++;
        }

        var path = DebugDumpHelper.WriteDumpLog("LayoutDump.log", sb);
        Debug.Log($"[BurstPQS] Layout dump written to {path}");
    }
}

internal class ForceFallbackToggle : DebugScreenToggle
{
    public override void SetupValues()
    {
        SetToggle(BatchPQS.ForceFallback);
    }

    public override void OnToggleChanged(bool state)
    {
        BatchPQS.ForceFallback = state;
        GetComponentInParent<BurstDebugScreenContent>()?.RefreshPlanetTable();
    }
}

internal class DumpLayoutButton : DebugScreenButton
{
    protected override void OnClick()
    {
        GetComponentInParent<BurstDebugScreenContent>()?.DumpLiveLayout();
    }
}
