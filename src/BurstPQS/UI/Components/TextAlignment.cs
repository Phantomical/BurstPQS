using TMPro;
using UnityEngine;

namespace BurstPQS.UI.Components;

/// <summary>
/// Sets TMP text alignment in Start() to work around a TMP bug where
/// alignment values get reset to TopLeft during Awake().
/// </summary>
internal class TextAlignment : MonoBehaviour
{
    public TextMeshProUGUI text;
    public TextAlignmentOptions alignment;

    void Start()
    {
        text.alignment = alignment;
    }
}
