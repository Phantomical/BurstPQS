using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.DebugUI;

/// <summary>
/// Base class for debug screen buttons, mirroring the pattern of
/// <see cref="KSP.UI.Screens.DebugToolbar.Screens.DebugScreenToggle"/> for toggles.
/// </summary>
internal abstract class DebugScreenButton : MonoBehaviour
{
    public Button button;

    void Awake()
    {
        button.onClick.AddListener(OnClick);
        SetupValues();
    }

    protected virtual void SetupValues() { }

    protected abstract void OnClick();
}
