using System.Linq;
using KSP.Localization;
using KSP.UI;
using KSP.UI.Screens;
using TMPro;
using Unity.Burst;
using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI;

[KSPAddon(KSPAddon.Startup.MainMenu, once: false)]
internal class BurstErrorPopup : MonoBehaviour
{
    private ApplicationLauncherButton _button;
    private GameObject _window;

    void Awake()
    {
        if (BurstCompiler.IsEnabled)
        {
            Destroy(this);
            enabled = false;
        }
    }

    void Start()
    {
        GameEvents.onGUIApplicationLauncherReady.Add(OnAppLauncherReady);

        if (ApplicationLauncher.Ready)
            OnAppLauncherReady();
    }

    void OnDestroy()
    {
        GameEvents.onGUIApplicationLauncherReady.Remove(OnAppLauncherReady);

        if (_window != null)
            Destroy(_window);

        if (_button != null)
            ApplicationLauncher.Instance.RemoveModApplication(_button);
    }

    private void OnAppLauncherReady()
    {
        if (_button != null)
            return;

        var texture = GameDatabase.Instance.GetTexture(
            "BurstPQS/Textures/burstpqs-warning-icon",
            asNormalMap: false
        );

        _button = ApplicationLauncher.Instance.AddModApplication(
            OnButtonTrue,
            OnButtonFalse,
            null,
            null,
            null,
            null,
            ApplicationLauncher.AppScenes.MAINMENU,
            texture
        );
    }

    private void OnButtonTrue()
    {
        if (_window == null)
            _window = BuildWindow(MainCanvasUtil.MainCanvas.transform);
        else
            _window.SetActive(true);
    }

    private void OnButtonFalse()
    {
        if (_window != null)
            _window.SetActive(false);
    }

    private GameObject BuildWindow(Transform parent)
    {
        var skin = UISkinManager.defaultSkin;

        // Root window
        var windowGo = Object.Instantiate(UIBuilder.Prefab("UIBoxPrefab"), parent);
        windowGo.name = "BurstPQSErrorWindow";

        var windowRect = windowGo.GetComponent<RectTransform>();
        windowRect.anchorMin = new Vector2(0.5f, 0.5f);
        windowRect.anchorMax = new Vector2(0.5f, 0.5f);
        windowRect.pivot = new Vector2(0.5f, 0.5f);
        windowRect.anchoredPosition = Vector2.zero;
        windowRect.sizeDelta = new Vector2(450, 250);

        var windowImage = windowGo.GetComponent<Image>();
        var opaqueBackground =
            skin.customStyles?.FirstOrDefault(s => s?.name == "List Item")?.normal?.background
            ?? skin.window.normal?.background;
        if (opaqueBackground != null)
            windowImage.sprite = opaqueBackground;
        windowImage.type = Image.Type.Sliced;

        windowGo.AddComponent<CanvasGroup>();
        windowGo.AddComponent<DragPanel>();

        var inputLock = windowGo.AddComponent<DialogMouseEnterControlLock>();
        inputLock.lockName = "BurstPQS_ErrorWindow";

        var windowLayout = windowGo.AddComponent<VerticalLayoutGroup>();
        windowLayout.padding = new RectOffset(8, 8, 8, 8);
        windowLayout.spacing = 8;
        windowLayout.childForceExpandWidth = true;
        windowLayout.childForceExpandHeight = false;
        windowLayout.childControlWidth = true;
        windowLayout.childControlHeight = true;

        // Title bar
        UIBuilder.CreateTitleBar(
            windowGo.transform,
            Localizer.Format("#BurstPQS_UI_BurstError_Title"),
            windowGo,
            skin,
            hideOnClose: true
        );

        // Warning message
        var messageGo = UIBuilder.CreateText(
            windowGo.transform,
            Localizer.Format("#BurstPQS_UI_BurstError_Message")
        );
        var messageTmp = messageGo.GetComponent<TextMeshProUGUI>();
        messageTmp.fontSize = 14;
        messageTmp.enableWordWrapping = true;
        var messageLE = messageGo.AddOrGetComponent<LayoutElement>();
        messageLE.flexibleWidth = 1;
        messageLE.flexibleHeight = 1;

        windowGo.SetActive(true);
        return windowGo;
    }
}
