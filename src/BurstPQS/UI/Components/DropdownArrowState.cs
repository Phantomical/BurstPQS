using TMPro;
using UnityEngine;

namespace BurstPQS.UI.Components;

/// <summary>
/// Flips a <see cref="TMP_Dropdown"/>'s arrow to point up while the list is open.
/// </summary>
/// <remarks>
/// TMP_Dropdown exposes no opened/closed event, so the state is polled. Rotating rather than
/// swapping sprites keeps this working with any chevron.
///
/// <see cref="TMP_Dropdown.IsExpanded"/> stays true while the list fades out, so the arrow
/// flips back as the fade finishes rather than the instant the list is dismissed.
/// </remarks>
internal class DropdownArrowState : MonoBehaviour
{
    public TMP_Dropdown dropdown;
    public RectTransform arrow;

    bool _expanded;

    void OnEnable()
    {
        _expanded = dropdown != null && dropdown.IsExpanded;
        Apply();
    }

    void Update()
    {
        if (dropdown == null)
            return;

        if (dropdown.IsExpanded == _expanded)
            return;

        _expanded = dropdown.IsExpanded;
        Apply();
    }

    void Apply()
    {
        if (arrow != null)
            arrow.localRotation = Quaternion.Euler(0f, 0f, _expanded ? 180f : 0f);
    }
}
