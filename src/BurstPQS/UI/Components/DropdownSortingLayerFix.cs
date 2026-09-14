using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.Components;

/// <summary>
/// Keeps a <see cref="TMP_Dropdown"/>'s popup on the same sorting layer as the canvas it
/// lives under.
/// </summary>
/// <remarks>
/// The popup's Canvas uses overrideSorting, so it takes its own sorting layer rather than
/// inheriting one, and TMP_Dropdown only ever sets sortingOrder. Sorting layer takes
/// precedence over sorting order, so a popup left on "Default" renders behind KSP's named
/// layers. The layer is only known once the dropdown is under a canvas.
/// </remarks>
internal class DropdownSortingLayerFix : MonoBehaviour
{
    /// <summary>The dropdown's template, which the popup is cloned from.</summary>
    public RectTransform template;

    void OnEnable()
    {
        if (template == null)
            return;

        var parentCanvas = GetComponentInParent<Canvas>();
        if (parentCanvas == null)
            return;

        var canvas = template.GetComponent<Canvas>();
        if (canvas == null)
            canvas = template.gameObject.AddComponent<Canvas>();

        canvas.overrideSorting = true;
        canvas.sortingLayerID = parentCanvas.rootCanvas.sortingLayerID;
        canvas.sortingOrder = 30000;
    }
}
