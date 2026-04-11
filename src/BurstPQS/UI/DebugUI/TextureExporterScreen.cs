using System;
using System.Collections;
using System.Collections.Generic;
using BurstPQS.Tools;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.DebugUI;

internal class TextureExporterScreen : MonoBehaviour
{
    [SerializeField]
    TMP_InputField _resolutionInput;

    [SerializeField]
    Toggle _exportHeight;

    [SerializeField]
    Toggle _exportColor;

    [SerializeField]
    Toggle _exportNormal;

    [SerializeField]
    Toggle _heightR16;

    [SerializeField]
    Toggle _orientNorthUp;

    [SerializeField]
    TextMeshProUGUI _statusLabel;

    [SerializeField]
    TextMeshProUGUI _planetLabel;

    [SerializeField]
    Button _exportCurrentButton;

    [SerializeField]
    Button _exportAllButton;

    List<CelestialBody> _bodies;
    int _selectedIndex;

    internal static RectTransform CreatePrefab()
    {
        var rt = DebugUIManager.CreateScreenPrefab<TextureExporterScreen>(
            "BurstPQS_TextureExporterScreen"
        );
        rt.GetComponent<TextureExporterScreen>().Build(rt);
        return rt;
    }

    const string HeaderText =
        "Export planetary surface textures (height, color, normal) for use in external tools. "
        + "Textures will be exported under <KSPDIR>/PluginData/BurstPQS. ";

    void Build(Transform parent)
    {
        DebugUIManager.CreateHeader(parent, "Texture Exporter");
        CreateSeparator(parent);

        var descLabel = DebugUIManager.CreateDirectLabel(parent, HeaderText);
        descLabel.fontStyle = FontStyles.Normal;
        descLabel.enableWordWrapping = true;

        CreateSeparator(parent);

        // Resolution row
        var resRow = DebugUIManager.CreateHorizontalLayout(parent);
        resRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        var resLabel = DebugUIManager.CreateLabel(resRow.transform, "Resolution:");
        resLabel.fontStyle = FontStyles.Normal;

        _resolutionInput = DebugUIManager.CreateInputField(resRow.transform);
        _resolutionInput.text = "4096";
        _resolutionInput.contentType = TMP_InputField.ContentType.IntegerNumber;
        _resolutionInput.characterLimit = 5;
        var resLayout = _resolutionInput.GetComponent<LayoutElement>();
        if (resLayout != null)
        {
            resLayout.preferredWidth = 100f;
            resLayout.flexibleWidth = 0f;
        }

        DebugUIManager.CreateHelpButton(
            resRow.transform,
            "Horizontal resolution of the exported textures.\n"
                + "Vertical resolution will be half this value.\n"
                + "Higher values take longer to export."
        );

        // Export options
        _exportHeight = DebugUIManager.CreateToggle(parent, "Export Height Map");
        _exportHeight.isOn = true;
        _heightR16 = DebugUIManager.CreateToggle(parent, "  16-bit Height (R16 DDS)");
        _heightR16.isOn = false;
        _exportColor = DebugUIManager.CreateToggle(parent, "Export Color Map");
        _exportColor.isOn = true;
        _exportNormal = DebugUIManager.CreateToggle(parent, "Export Normal Map");
        _exportNormal.isOn = true;

        var northUpRow = DebugUIManager.CreateHorizontalLayout(parent);
        northUpRow.GetComponent<HorizontalLayoutGroup>().childForceExpandWidth = false;
        _orientNorthUp = DebugUIManager.CreateToggle(northUpRow.transform, "Orient North Up");
        _orientNorthUp.isOn = false;
        DebugUIManager.CreateHelpButton(
            northUpRow.transform,
            "By default, textures are oriented with north at the\n"
                + "bottom to be compatible with how KSP uses them.\n"
                + "Enable this to flip textures so north is at the top."
        );

        // Planet selector row: [Export Planet] [Planet Selector]
        var planetRow = DebugUIManager.CreateHorizontalLayout(parent);
        _exportCurrentButton = DebugUIManager
            .CreateButton<ExportCurrentButton>(planetRow.transform, "Export Planet")
            .button;

        var selector = DebugUIManager.CreateButton<PlanetSelectorButton>(
            planetRow.transform,
            "---"
        );
        _planetLabel = selector.GetComponentInChildren<TextMeshProUGUI>();

        // Export all row
        _exportAllButton = DebugUIManager
            .CreateButton<ExportAllButton>(parent, "Export All Planets")
            .button;

        // Status
        _statusLabel = DebugUIManager.CreateLabel(parent, "Idle");
        _statusLabel.fontStyle = FontStyles.Normal;
    }

    void Start()
    {
        _bodies = [];
        foreach (var body in FlightGlobals.Bodies)
        {
            if (body.pqsController != null)
                _bodies.Add(body);
        }

        var current = FlightGlobals.currentMainBody ?? FlightGlobals.GetHomeBody();
        _selectedIndex = current != null ? _bodies.IndexOf(current) : 0;
        if (_selectedIndex < 0)
            _selectedIndex = 0;
    }

    void Update()
    {
        _planetLabel.text = _bodies is { Count: > 0 }
            ? _bodies[_selectedIndex].bodyDisplayName.LocalizeRemoveGender()
            : "---";

        _statusLabel.text = TextureExporter.StatusMessage;

        bool exporting = TextureExporter.IsExporting;
        _exportCurrentButton.interactable = !exporting;
        _exportAllButton.interactable = !exporting;
    }

    internal void CyclePlanet(int delta)
    {
        if (_bodies == null || _bodies.Count == 0)
            return;
        _selectedIndex = (_selectedIndex + delta + _bodies.Count) % _bodies.Count;
    }

    TextureExportOptions GetOptions()
    {
        int resolution = 4096;
        if (_resolutionInput != null && int.TryParse(_resolutionInput.text, out int parsed))
            resolution = Mathf.Clamp(parsed, 64, 16384);

        return new TextureExportOptions
        {
            width = resolution,
            height = resolution / 2,
            exportHeight = _exportHeight != null && _exportHeight.isOn,
            exportColor = _exportColor != null && _exportColor.isOn,
            exportNormal = _exportNormal != null && _exportNormal.isOn,
            heightFormat =
                _heightR16 != null && _heightR16.isOn ? HeightFormat.R16 : HeightFormat.RGB24,
            orientNorthUp = _orientNorthUp != null && _orientNorthUp.isOn,
        };
    }

    internal void StartExportPlanet()
    {
        if (_bodies == null || _bodies.Count == 0)
        {
            ScreenMessages.PostScreenMessage("No planets available", 3f);
            return;
        }

        StartCoroutine(ExportPlanet());
    }

    IEnumerator ExportPlanet()
    {
        var options = GetOptions();
        var body = _bodies[_selectedIndex];
        using var guard = new TextureExporter.ExportGuard();

        yield return TextureExporter.ExportPlanet(body, options);
    }

    internal void StartExportAll()
    {
        StartCoroutine(ExportAll());
    }

    IEnumerator ExportAll()
    {
        var coroutines = new Queue<Coroutine>();
        var options = GetOptions();
        using var guard = new TextureExporter.ExportGuard();

        foreach (var body in _bodies)
            coroutines.Enqueue(StartCoroutine(TextureExporter.ExportPlanet(body, options)));

        foreach (var coroutine in coroutines)
            yield return coroutine;
    }

    #region UI Helpers
    static void CreateSeparator(Transform parent)
    {
        var go = new GameObject("Separator", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var img = go.AddComponent<Image>();
        img.color = new Color(0.4f, 0.4f, 0.4f);
        var le = go.AddComponent<LayoutElement>();
        le.preferredHeight = 1f;
        le.flexibleWidth = 1f;
    }
    #endregion
}

internal class PlanetSelectorButton : DebugScreenButton
{
    protected override void OnClick()
    {
        GetComponentInParent<TextureExporterScreen>()?.CyclePlanet(1);
    }
}

internal class ExportCurrentButton : DebugScreenButton
{
    protected override void OnClick()
    {
        GetComponentInParent<TextureExporterScreen>()?.StartExportPlanet();
    }
}

internal class ExportAllButton : DebugScreenButton
{
    protected override void OnClick()
    {
        GetComponentInParent<TextureExporterScreen>()?.StartExportAll();
    }
}
