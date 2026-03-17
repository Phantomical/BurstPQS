using UnityEngine;
using UnityEngine.UI;

namespace BurstPQS.UI.Components;

internal class HideButton : MonoBehaviour
{
    public Button button;
    public GameObject target;

    void Start()
    {
        button.onClick.AddListener(OnClick);
    }

    void OnClick()
    {
        target.SetActive(false);
    }
}
