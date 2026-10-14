using UnityEngine;
using UnityEngine.UI;

namespace PsdUgui
{
[RequireComponent(typeof(Toggle))]
public sealed class ToggleStateAdapter : MonoBehaviour
{
    [SerializeField] private GameObject onObject;
    [SerializeField] private GameObject offObject;
    private Toggle toggle;

    public void SetTargets(GameObject selected, GameObject unselected)
    {
        onObject = selected;
        offObject = unselected;
        Refresh(GetComponent<Toggle>().isOn);
    }

    private void OnEnable()
    {
        toggle = GetComponent<Toggle>();
        toggle.onValueChanged.AddListener(Refresh);
        Refresh(toggle.isOn);
    }

    private void OnDisable()
    {
        if (toggle != null)
            toggle.onValueChanged.RemoveListener(Refresh);
    }

    private void LateUpdate()
    {
        // 同时支持 SetIsOnWithoutNotify 更新选中状态。
        Refresh(toggle.isOn);
    }

    private void Refresh(bool isOn)
    {
        if (onObject != null && onObject.activeSelf != isOn)
            onObject.SetActive(isOn);
        if (offObject != null && offObject.activeSelf == isOn)
            offObject.SetActive(!isOn);
    }
}

}
