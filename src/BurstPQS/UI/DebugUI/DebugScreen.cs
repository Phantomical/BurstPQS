using System.Text;
using BurstPQS.UI.Components;
using KSP.UI.Screens.DebugToolbar;
using KSP.UI.Screens.DebugToolbar.Screens;
using TMPro;
using Unity.Burst;
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
    static readonly Color StatusGood = new(0.35f, 1.0f, 0.35f);
    static readonly Color StatusBad = new(1.0f, 0.35f, 0.35f);
    static readonly Color SeparatorColor = new(0.4f, 0.4f, 0.4f);

    const string HeaderSettings = "Settings";
    const string HeaderPlanets = "Planets";
    const string LabelBurstCompilation = "Burst Compilation";
    const string LabelEnabled = "Enabled";
    const string LabelDisabled = "Disabled";
    const string LabelBurst = "BurstPQS";
    const string LabelFallback = "Fallback";
    const string LabelForceFallback = "Force Fallback Mode";
    const string TooltipBurstCompilation = """
        Whether burst compilation is working correctly.

        If this says disabled then many things will be much slower than they
        are meant to be. This won't break your game but you won't get many of
        the benefits of BurstPQS.

        To troubleshoot this:
        * Make sure you have the "Burst Compiler" package installed in CKAN.
          Manual installs of KSPBurst will have this by default.
        * If on linux make sure you have mono installed, even if you are running
          KSP in Proton.

        If none of the above work check the BurstPQS and KSPBurst forum threads
        for help and the latest advice.
        """;
    const string TooltipForceFallback = """
        Force all planets to use the stock terrain system instead of BurstPQS.

        This is mainly useful for comparing the implementation of PQSMods
        between BurstPQS and stock. If you're not doing that then you can ignore
        this option.
        """;
    const string TooltipPlanets = """
        Shows whether BurstPQS is enabled for each planetary body.

        * Burst - Everything is supported for this planet. This is the one you want.
        * Fallback - Something is not supported and BurstPQS is disabled for this planet.

        There will be a more detailed error message in KSP.log that will
        say exactly what caused BurstPQS to use the fallback implementation
        for each planet that is marked as Fallback here.
        """;

    [SerializeField]
    private TableLayoutGroup _planetTable;

    internal static RectTransform CreatePrefab()
    {
        var rt = DebugUIManager.CreateScreenPrefab<BurstDebugScreenContent>("BurstPQS_DebugScreen");
        var content = rt.GetComponent<BurstDebugScreenContent>();

        // Replace the root VLG with a ScrollRect that wraps all content.
        var rootVlg = rt.GetComponent<VerticalLayoutGroup>();
        var rootPadding = rootVlg.padding;
        var rootSpacing = rootVlg.spacing;
        Object.DestroyImmediate(rootVlg);

        var scrollRect = rt.gameObject.AddComponent<ScrollRect>();
        scrollRect.horizontal = false;
        scrollRect.vertical = true;
        scrollRect.scrollSensitivity = 20f;
        scrollRect.movementType = ScrollRect.MovementType.Clamped;

        var viewportGo = new GameObject("Viewport", typeof(RectTransform));
        viewportGo.transform.SetParent(rt, false);
        var vp = viewportGo.GetComponent<RectTransform>();
        vp.anchorMin = Vector2.zero;
        vp.anchorMax = Vector2.one;
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

        var contentVlg = contentGo.AddComponent<VerticalLayoutGroup>();
        contentVlg.childAlignment = TextAnchor.UpperLeft;
        contentVlg.childControlWidth = true;
        contentVlg.childControlHeight = true;
        contentVlg.childForceExpandWidth = true;
        contentVlg.childForceExpandHeight = false;
        contentVlg.spacing = rootSpacing;
        contentVlg.padding = rootPadding;

        var csf = contentGo.AddComponent<ContentSizeFitter>();
        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        scrollRect.content = cr;

        DebugUIManager.CreateScrollbar(rt, scrollRect);

        content.BuildSettingsSection(cr);
        content.BuildPlanetsSection(cr);
        return rt;
    }

    private void BuildSettingsSection(Transform parent)
    {
        DebugUIManager.CreateHeader(parent, HeaderSettings);
        CreateSeparator(parent);

        var burstStatusRow = DebugUIManager.CreateHorizontalLayout(parent);
        burstStatusRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        var burstCompilationLabel = DebugUIManager.CreateLabel(
            burstStatusRow.transform,
            LabelBurstCompilation
        );
        burstCompilationLabel.fontStyle = FontStyles.Normal;
        var burstEnabled = BurstCompiler.IsEnabled;
        var burstStatusValue = DebugUIManager.CreateLabel(
            burstStatusRow.transform,
            burstEnabled ? LabelEnabled : LabelDisabled
        );
        burstStatusValue.color = burstEnabled ? StatusGood : StatusBad;
        DebugUIManager.CreateHelpButton(burstStatusRow.transform, TooltipBurstCompilation);

        var toggleRow = DebugUIManager.CreateHorizontalLayout(parent);
        toggleRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        DebugUIManager.CreateToggle<ForceFallbackToggle>(toggleRow.transform, LabelForceFallback);
        DebugUIManager.CreateHelpButton(toggleRow.transform, TooltipForceFallback);
    }

    private void BuildPlanetsSection(Transform parent)
    {
        DebugUIManager.CreateSpacer(parent);

        var headerRow = DebugUIManager.CreateHorizontalLayout(parent);
        headerRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        DebugUIManager.CreateHeader(headerRow.transform, HeaderPlanets);
        DebugUIManager.CreateHelpButton(headerRow.transform, TooltipPlanets);

        CreateSeparator(parent);

        var tableGo = new GameObject("PlanetTable", typeof(RectTransform));
        tableGo.transform.SetParent(parent, false);
        var tlg = tableGo.AddComponent<TableLayoutGroup>();
        tlg.MinimumColumnWidth = 0f;
        tlg.ColumnSpacing = 8f;
        tlg.RowSpacing = 2f;

        _planetTable = tlg;
    }

    void OnEnable() => PopulatePlanetTable();

    void OnDisable() => ClearPlanetTable();

    internal void RefreshPlanetTable()
    {
        ClearPlanetTable();
        PopulatePlanetTable();
    }

    struct TableEntry
    {
        public string name;
        public bool fallback;
        public string tooltip;
    }

    private void PopulatePlanetTable()
    {
        if (FlightGlobals.Bodies == null)
            return;

        var bodies = new System.Collections.Generic.List<TableEntry>();
        foreach (var body in FlightGlobals.Bodies)
        {
            if (body.pqsController == null)
                continue;
            var batchPQS = body.pqsController.GetComponent<BatchPQS>();
            bodies.Add(
                new TableEntry
                {
                    name = body.bodyDisplayName.Replace("^N", ""),
                    fallback = batchPQS == null || batchPQS.Fallback,
                    tooltip =
                        batchPQS?.FallbackMessage ?? "No BatchPQS instance found for this planet",
                }
            );
        }

        // Set row heights before adding children so TableLayoutGroup knows the row count.
        var heights = new float[bodies.Count];
        for (int i = 0; i < heights.Length; i++)
            heights[i] = 24f;
        _planetTable.RowHeights = heights;

        foreach (var entry in bodies)
        {
            DebugUIManager.CreateDirectLabel(_planetTable.transform, entry.name);

            var statusRow = DebugUIManager.CreateHorizontalLayout(_planetTable.transform);
            var hlg = statusRow.GetComponent<HorizontalLayoutGroup>();
            hlg.childForceExpandWidth = false;
            statusRow.GetComponent<LayoutElement>().minHeight = -1f;
            var statusTmp = DebugUIManager.CreateDirectLabel(
                statusRow.transform,
                entry.fallback ? LabelFallback : LabelBurst
            );
            statusTmp.color = entry.fallback ? StatusBad : StatusGood;

            var spacer = new GameObject("FlexSpacer", typeof(RectTransform));
            spacer.transform.SetParent(statusRow.transform, false);
            var spacerLE = spacer.AddComponent<LayoutElement>();
            spacerLE.flexibleWidth = 1f;

            DebugUIManager.CreateHelpButton(statusRow.transform, entry.tooltip);
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
        img.color = SeparatorColor;
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
