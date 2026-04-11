using KSP.UI;
using KSP.UI.Screens.DebugToolbar;
using KSP.UI.Screens.DebugToolbar.Screens;
using KSP.UI.TooltipTypes;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.DebugUI;

/// <summary>
/// Finds and caches UI prefab templates from existing KSP debug screens so our custom
/// debug screen uses the same visual theme. Mirrors KSPTextureLoader's DebugUIManager.
/// </summary>
internal static class DebugUIManager
{
    static GameObject _labelPrefab;
    static GameObject _buttonPrefab;
    static GameObject _togglePrefab;
    static GameObject _inputFieldPrefab;
    static GameObject _scrollbarPrefab;
    static GameObject _spacerPrefab;

    static bool _initialized;

    /// <summary>
    /// Must be called after DebugScreenSpawner is set up (e.g. from MainMenu Start()).
    /// </summary>
    public static bool Initialize()
    {
        if (_initialized)
            return true;

        var spawner = DebugScreenSpawner.Instance;
        if (spawner == null)
        {
            Debug.LogWarning("[BurstPQS] DebugUIManager: DebugScreenSpawner.Instance is null");
            return false;
        }

        var screens = spawner.debugScreens?.screens;
        if (screens == null)
        {
            Debug.LogWarning("[BurstPQS] DebugUIManager: No debug screens found");
            return false;
        }

        foreach (var wrapper in screens)
        {
            if (wrapper.screen == null)
                continue;

            var root = wrapper.screen.gameObject;
            switch (wrapper.name)
            {
                case "Debug":
                    FindConsolePrefabs(root);
                    break;
                case "Database":
                    FindDatabasePrefabs(root);
                    break;
                case "Debugging":
                    FindDebuggingPrefabs(root);
                    break;
            }
        }

        // Scrollbar — from sidebar scroll view in the screen prefab
        if (_scrollbarPrefab == null && spawner.screenPrefab != null)
        {
            var scrollbar = spawner.screenPrefab.transform.Find(
                "VerticalLayout/HorizontalLayout/Contents/Contents Scroll View/Scrollbar"
            );
            if (scrollbar != null)
                _scrollbarPrefab = ClonePrefab(scrollbar.gameObject, "BurstPQS_ScrollbarPrefab");
        }

        // Spacer — created from scratch (no suitable prefab)
        if (_spacerPrefab == null)
        {
            _spacerPrefab = new GameObject("BurstPQS_SpacerPrefab", typeof(RectTransform));
            _spacerPrefab.SetActive(false);
            var le = _spacerPrefab.AddComponent<LayoutElement>();
            le.preferredHeight = 8f;
            Object.DontDestroyOnLoad(_spacerPrefab);
        }

        _initialized =
            _labelPrefab != null
            && _buttonPrefab != null
            && _togglePrefab != null
            && _inputFieldPrefab != null;

        if (!_initialized)
            Debug.LogWarning(
                $"[BurstPQS] DebugUIManager: Failed to find all prefabs. "
                    + $"label={_labelPrefab != null}, button={_buttonPrefab != null}, "
                    + $"toggle={_togglePrefab != null}, inputField={_inputFieldPrefab != null}"
            );

        return _initialized;
    }

    /// <summary>
    /// "Debug" console screen — button in BottomBar.
    /// </summary>
    static void FindConsolePrefabs(GameObject root)
    {
        var bottomBar = root.transform.Find("BottomBar");
        if (bottomBar == null)
            return;

        if (_buttonPrefab == null)
        {
            var buttonGo = bottomBar.Find("Button");
            if (buttonGo != null)
                _buttonPrefab = ClonePrefab(buttonGo.gameObject, "BurstPQS_ButtonPrefab");
        }

        if (_inputFieldPrefab == null)
        {
            var inputFieldGo = bottomBar.Find("InputField");
            if (inputFieldGo != null)
                _inputFieldPrefab = ClonePrefab(
                    inputFieldGo.gameObject,
                    "BurstPQS_InputFieldPrefab"
                );
        }
    }

    /// <summary>
    /// "Database" screen — TotalLabel for a label template.
    /// </summary>
    static void FindDatabasePrefabs(GameObject root)
    {
        if (_labelPrefab != null)
            return;

        var totalLabel = root.transform.Find("TotalLabel");
        if (totalLabel != null)
            _labelPrefab = ClonePrefab(totalLabel.gameObject, "BurstPQS_LabelPrefab");
    }

    /// <summary>
    /// "Debugging" screen — PrintErrorsToScreen toggle wrapper.
    /// Strips the KSP-specific DebugScreenToggle component so we can attach our own.
    /// </summary>
    static void FindDebuggingPrefabs(GameObject root)
    {
        if (_togglePrefab != null)
            return;

        var toggleWrapper = root.transform.Find("PrintErrorsToScreen");
        if (toggleWrapper == null)
            return;

        _togglePrefab = ClonePrefab(toggleWrapper.gameObject, "BurstPQS_TogglePrefab");

        var existing = _togglePrefab.GetComponent<DebugScreenToggle>();
        if (existing != null)
            Object.DestroyImmediate(existing);
    }

    /// <summary>
    /// The toggle prefab's inner Toggle child has a fixed width. Stretch it to fill the wrapper.
    /// </summary>
    static void StretchToggleChild(GameObject wrapper)
    {
        var innerToggle = wrapper.GetComponentInChildren<Toggle>(true);
        if (innerToggle == null)
            return;

        var rt = innerToggle.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    static GameObject ClonePrefab(GameObject source, string name)
    {
        var clone = Object.Instantiate(source);
        clone.name = name;
        clone.SetActive(false);
        Object.DontDestroyOnLoad(clone);
        return clone;
    }

    // ── Factory methods ──────────────────────────────────────────────────────

    public static RectTransform CreateScreenPrefab<T>(string name)
        where T : MonoBehaviour
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.SetActive(false);
        Object.DontDestroyOnLoad(go);
        go.AddComponent<T>();

        var rt = go.GetComponent<RectTransform>();
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        var vlg = go.AddComponent<VerticalLayoutGroup>();
        vlg.childAlignment = TextAnchor.UpperLeft;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.spacing = 4f;
        vlg.padding = new RectOffset(8, 8, 8, 8);

        return rt;
    }

    /// <summary>
    /// Creates a label with TMP directly on the GO (no wrapper), so the layout group
    /// sees TMP's own ILayoutElement and auto-sizes to text content.
    /// Optionally pins the width via a LayoutElement.
    /// </summary>
    public static TextMeshProUGUI CreateDirectLabel(Transform parent, string text)
    {
        var sourceTmp = _labelPrefab.GetComponentInChildren<TextMeshProUGUI>();
        var go = new GameObject("Label", typeof(RectTransform));
        go.transform.SetParent(parent, false);
        var tmp = go.AddComponent<TextMeshProUGUI>();
        tmp.font = sourceTmp.font;
        tmp.fontSize = sourceTmp.fontSize;
        tmp.color = sourceTmp.color;
        tmp.overflowMode = sourceTmp.overflowMode;
        tmp.text = text;
        return tmp;
    }

    public static TextMeshProUGUI CreateLabel(Transform parent, string text)
    {
        var go = Object.Instantiate(_labelPrefab, parent, false);
        go.SetActive(true);
        go.name = "Label";

        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
        }

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        var textRect = tmp.GetComponent<RectTransform>();
        textRect.anchorMin = Vector2.zero;
        textRect.anchorMax = Vector2.one;
        textRect.offsetMin = Vector2.zero;
        textRect.offsetMax = Vector2.zero;

        tmp.text = text;
        return tmp;
    }

    public static TextMeshProUGUI CreateHeader(Transform parent, string text)
    {
        var tmp = CreateLabel(parent, text);
        tmp.fontStyle = FontStyles.Bold;
        tmp.fontSize *= 1.2f;
        return tmp;
    }

    public static T CreateToggle<T>(Transform parent, string label)
        where T : DebugScreenToggle
    {
        var go = Object.Instantiate(_togglePrefab, parent, false);
        go.name = "Toggle";

        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
        }

        StretchToggleChild(go);

        var component = go.AddComponent<T>();
        component.toggle = go.GetComponentInChildren<Toggle>();
        var labelTransform = component.toggle?.transform.Find("Label");
        if (labelTransform != null)
            component.toggleText = labelTransform.GetComponent<TextMeshProUGUI>();
        component.text = label;

        go.SetActive(true);
        return component;
    }

    public static Toggle CreateToggle(Transform parent, string label)
    {
        var go = Object.Instantiate(_togglePrefab, parent, false);
        go.name = "Toggle";

        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
        }

        StretchToggleChild(go);
        go.SetActive(true);

        var toggle = go.GetComponentInChildren<Toggle>();

        var labelTransform = toggle?.transform.Find("Label");
        if (labelTransform != null)
        {
            var tmp = labelTransform.GetComponent<TextMeshProUGUI>();
            if (tmp != null)
                tmp.text = label;
        }

        return toggle;
    }

    public static GameObject CreateHelpButton(Transform parent, string tooltip)
    {
        var go = Object.Instantiate(_buttonPrefab, parent, false);
        go.name = "HelpButton";
        go.SetActive(true);

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = "?";

        var layout = go.GetComponent<LayoutElement>();
        if (layout == null)
            layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = 24f;
        layout.preferredHeight = 24f;
        layout.minHeight = -1f;
        layout.flexibleWidth = 0f;

        if (!string.IsNullOrEmpty(tooltip))
        {
            var tooltipPrefab = UISkinManager
                .GetPrefab("UISliderPrefab")
                .GetComponent<TooltipController_Text>()
                .prefab;
            var controller = go.AddComponent<TooltipController_Text>();
            controller.prefab = tooltipPrefab;
            controller.textString = tooltip;
        }

        return go;
    }

    public static T CreateButton<T>(Transform parent, string text)
        where T : DebugScreenButton
    {
        var go = Object.Instantiate(_buttonPrefab, parent, false);
        go.name = "Button";

        SetupButtonLayout(go);

        var tmp = go.GetComponentInChildren<TextMeshProUGUI>();
        if (tmp != null)
            tmp.text = text;

        var component = go.AddComponent<T>();
        component.button = go.GetComponent<Button>();

        go.SetActive(true);
        return component;
    }

    static void SetupButtonLayout(GameObject go)
    {
        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
            layout.minHeight = 30f;
        }
    }

    public static TMP_InputField CreateInputField(Transform parent)
    {
        var go = Object.Instantiate(_inputFieldPrefab, parent, false);
        go.SetActive(true);
        go.name = "InputField";

        SetupInputFieldPrefab(go);

        var input = go.GetComponent<TMP_InputField>();
        input.text = "";

        return input;
    }

    static void SetupInputFieldPrefab(GameObject go)
    {
        var layout = go.GetComponent<LayoutElement>();
        if (layout != null)
        {
            layout.preferredWidth = -1;
            layout.flexibleWidth = 1;
            layout.minHeight = 30f;
        }

        var input = go.GetComponent<TMP_InputField>();
        if (input?.textComponent != null)
            input.textComponent.alignment = TextAlignmentOptions.Left;
    }

    public static void CreateSpacer(Transform parent, float height = 8f)
    {
        var go = Object.Instantiate(_spacerPrefab, parent, false);
        go.SetActive(true);
        go.name = "Spacer";
        var layout = go.GetComponent<LayoutElement>();
        layout.preferredHeight = height;
        layout.minHeight = height;
    }

    public static GameObject CreateHorizontalLayout(Transform parent, float spacing = 8f)
    {
        var go = new GameObject("HorizontalLayout", typeof(RectTransform));
        go.transform.SetParent(parent, false);

        var hlg = go.AddComponent<HorizontalLayoutGroup>();
        hlg.spacing = spacing;
        hlg.childAlignment = TextAnchor.MiddleLeft;
        hlg.childControlWidth = true;
        hlg.childControlHeight = true;
        hlg.childForceExpandWidth = true;
        hlg.childForceExpandHeight = false;

        var layout = go.AddComponent<LayoutElement>();
        layout.minHeight = 30f;

        return go;
    }

    public static Scrollbar CreateScrollbar(Transform parent, ScrollRect scrollRect)
    {
        if (_scrollbarPrefab == null)
            return null;

        var go = Object.Instantiate(_scrollbarPrefab, parent, false);
        go.SetActive(true);
        go.name = "Scrollbar";

        var scrollbar = go.GetComponent<Scrollbar>();
        scrollbar.direction = Scrollbar.Direction.BottomToTop;

        scrollRect.verticalScrollbar = scrollbar;
        scrollRect.verticalScrollbarVisibility = ScrollRect
            .ScrollbarVisibility
            .AutoHideAndExpandViewport;
        scrollRect.verticalScrollbarSpacing = 0f;

        return scrollbar;
    }
}
