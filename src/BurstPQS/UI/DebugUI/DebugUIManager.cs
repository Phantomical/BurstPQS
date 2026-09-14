using BurstPQS.UI.Components;
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
    static Sprite _arrowSprite;

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

        // Dropdown arrow
        if (_arrowSprite == null)
        {
            var treeItem = spawner.screenPrefab?.treeView?.itemPrefab;
            _arrowSprite = FindDropdownArrow(treeItem?.spriteExpanded);
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

    /// <summary>
    /// Finds "dropdown_arrow", the chevron stock TMP_Dropdowns use, falling back to
    /// <paramref name="fallback"/> when it is not loaded.
    /// </summary>
    /// <remarks>
    /// The sprite is only reachable through prefabs the debug screen cannot get at, and
    /// AssetBase does not index it, so it is located by scanning the loaded sprites.
    /// </remarks>
    static Sprite FindDropdownArrow(Sprite fallback)
    {
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
        {
            if (sprite.name == "dropdown_arrow")
                return sprite;
        }

        return fallback;
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

    public static TMP_Dropdown CreateDropdown(Transform parent)
    {
        var buttonImage = _buttonPrefab.GetComponent<Image>();
        var buttonSelectable = _buttonPrefab.GetComponent<Button>();
        var panelImage = _inputFieldPrefab.GetComponent<Image>();
        // Caption and rows sit on the button sprite, so they use the button's label colour.
        // _labelPrefab's colour is for the dark window background.
        var sourceTmp = _buttonPrefab.GetComponentInChildren<TextMeshProUGUI>(true);
        var checkmarkImage = _togglePrefab.GetComponentInChildren<Toggle>(true)?.graphic as Image;

        Image scrollbarTrack = null;
        Image scrollbarHandle = null;
        if (_scrollbarPrefab != null)
        {
            scrollbarTrack = _scrollbarPrefab.GetComponent<Image>();
            scrollbarHandle = _scrollbarPrefab.GetComponent<Scrollbar>()?.targetGraphic as Image;
        }

        var go = TMP_DefaultControls.CreateDropdown(
            new TMP_DefaultControls.Resources
            {
                standard = buttonImage?.sprite,
                background = scrollbarTrack?.sprite,
                checkmark = checkmarkImage?.sprite,
                dropdown = _arrowSprite,
            }
        );
        go.name = "Dropdown";
        go.transform.SetParent(parent, false);
        // The popup has its own Canvas, and a sub-canvas is culled unless its layer is one the
        // UI camera renders. Layout containers are bare GameObjects on layer 0, so the layer
        // comes from a harvested prefab rather than from parent.
        SetLayerRecursively(go, _buttonPrefab.layer);

        var dropdown = go.GetComponent<TMP_Dropdown>();
        dropdown.ClearOptions();

        // Closed state: look and behave like the buttons it sits beside.
        CopyImageStyle(go.GetComponent<Image>(), buttonImage);
        if (buttonSelectable != null)
        {
            dropdown.transition = buttonSelectable.transition;
            dropdown.colors = buttonSelectable.colors;
            dropdown.spriteState = buttonSelectable.spriteState;
        }

        var layout = go.AddComponent<LayoutElement>();
        layout.preferredWidth = -1f;
        layout.flexibleWidth = 1f;
        layout.minHeight = 30f;

        // The stock insets leave 17px of a 30px row, which clips descenders in the KSP font.
        // Symmetric horizontal insets put centred text on the control's centre line while
        // still keeping long names out from under the arrow.
        var labelRect = (RectTransform)go.transform.Find("Label");
        labelRect.offsetMin = new Vector2(24f, 2f);
        labelRect.offsetMax = new Vector2(-24f, -2f);
        StyleDropdownText(dropdown.captionText, sourceTmp);

        // An Image with no sprite draws a white box, so drop the arrow when there is none.
        var arrow = go.transform.Find("Arrow").GetComponent<Image>();
        if (_arrowSprite == null)
        {
            arrow.gameObject.SetActive(false);
        }
        else
        {
            if (sourceTmp != null)
                arrow.color = sourceTmp.color;

            var arrowState = go.AddComponent<DropdownArrowState>();
            arrowState.dropdown = dropdown;
            arrowState.arrow = (RectTransform)arrow.transform;
        }

        var template = (RectTransform)go.transform.Find("Template");
        template.sizeDelta = new Vector2(template.sizeDelta.x, 200f);

        // TMP_DefaultControls leaves this at Unity's default of 1, roughly a pixel per wheel
        // notch. KSP's own dropdowns use 15.
        template.GetComponent<ScrollRect>().scrollSensitivity = 15f;
        CopyImageStyle(template.GetComponent<Image>(), panelImage ?? buttonImage);

        // Mask clips by the sprite's alpha; a plain quad keeps the list corners square.
        var viewport = template.Find("Viewport").GetComponent<Image>();
        viewport.sprite = null;
        viewport.type = Image.Type.Simple;
        viewport.color = Color.white;

        // SetupTemplate would add this on first open and leave it on the "Default" sorting
        // layer. Creating it here gives DropdownSortingLayerFix something to correct before
        // the popup is cloned.
        var templateCanvas = template.gameObject.AddComponent<Canvas>();
        templateCanvas.overrideSorting = true;
        templateCanvas.sortingOrder = 30000;
        template.gameObject.AddComponent<GraphicRaycaster>();
        go.AddComponent<DropdownSortingLayerFix>().template = template;

        var item = (RectTransform)template.Find("Viewport/Content/Item");
        item.sizeDelta = new Vector2(item.sizeDelta.x, 24f);

        // Rows get the button treatment too, so hovering one highlights it.
        var itemToggle = item.GetComponent<Toggle>();
        CopyImageStyle(itemToggle.targetGraphic as Image, buttonImage);
        if (buttonSelectable != null)
        {
            itemToggle.transition = buttonSelectable.transition;
            itemToggle.colors = buttonSelectable.colors;
            itemToggle.spriteState = buttonSelectable.spriteState;
        }
        // Mirror the checkmark's gutter on the right so row text centres on the row.
        var itemLabelRect = (RectTransform)item.Find("Item Label");
        itemLabelRect.offsetMin = new Vector2(20f, 1f);
        itemLabelRect.offsetMax = new Vector2(-20f, -2f);
        StyleDropdownText(dropdown.itemText, sourceTmp);

        var itemCheckmark = itemToggle.graphic as Image;
        if (checkmarkImage != null)
            CopyImageStyle(itemCheckmark, checkmarkImage);
        else if (itemCheckmark != null)
            itemCheckmark.gameObject.SetActive(false);

        CopyImageStyle(template.Find("Scrollbar").GetComponent<Image>(), scrollbarTrack);
        CopyImageStyle(
            template.Find("Scrollbar/Sliding Area/Handle").GetComponent<Image>(),
            scrollbarHandle
        );

        return dropdown;
    }

    static void StyleDropdownText(TMP_Text text, TextMeshProUGUI source)
    {
        if (text == null || source == null)
            return;

        text.font = source.font;
        text.fontSize = source.fontSize;
        text.color = source.color;
        text.alignment = TextAlignmentOptions.Center;
        text.enableWordWrapping = false;
        text.overflowMode = TextOverflowModes.Ellipsis;
    }

    static void CopyImageStyle(Image dest, Image source)
    {
        if (dest == null || source == null)
            return;

        dest.sprite = source.sprite;
        dest.type = source.type;
        dest.color = source.color;
        dest.material = source.material;
        dest.fillCenter = source.fillCenter;
        dest.pixelsPerUnitMultiplier = source.pixelsPerUnitMultiplier;
    }

    static void SetLayerRecursively(GameObject go, int layer)
    {
        go.layer = layer;
        for (int i = 0; i < go.transform.childCount; i++)
            SetLayerRecursively(go.transform.GetChild(i).gameObject, layer);
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
