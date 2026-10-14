using UnityEngine;

namespace PsdUgui
{
[RequireComponent(typeof(RectTransform))]
public class TopAreaAdapter : MonoBehaviour
{
    private RectTransform rectTransform;
    private Vector2 originalPosition;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        originalPosition = rectTransform.anchoredPosition;
    }

    private void OnEnable()
    {
        Apply();
    }

    private void LateUpdate()
    {
        // 同时响应安全区、屏幕方向及父节点布局变化。
        Apply();
    }

    private void OnDisable()
    {
        rectTransform.anchoredPosition = originalPosition;
    }

    private void Apply()
    {
        var parent = rectTransform.parent as RectTransform;
        Vector2 topCenter = new Vector2(0.5f, 1f);
        if (parent == null || rectTransform.anchorMin != topCenter ||
            rectTransform.anchorMax != topCenter || Screen.height <= 0)
            return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null)
            return;
        Canvas rootCanvas = canvas.rootCanvas;
        if (rootCanvas.renderMode == RenderMode.WorldSpace)
            return;
        Camera camera = rootCanvas.renderMode == RenderMode.ScreenSpaceOverlay
            ? null : rootCanvas.worldCamera;
        if (rootCanvas.renderMode == RenderMode.ScreenSpaceCamera && camera == null)
            return;

        // 以父节点上中锚点为基准，避免父节点已适配安全区时重复避让。
        Vector2 anchorScreen = RectTransformUtility.WorldToScreenPoint(camera,
            parent.TransformPoint(new Vector3(parent.rect.center.x, parent.rect.yMax, 0f)));
        Vector2 safeScreen = new Vector2(anchorScreen.x,
            Mathf.Min(anchorScreen.y, Screen.safeArea.yMax));
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, anchorScreen,
                camera, out Vector2 anchorLocal) ||
            !RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, safeScreen,
                camera, out Vector2 safeLocal))
            return;

        Vector2 position = originalPosition + new Vector2(0f, safeLocal.y - anchorLocal.y);
        if (rectTransform.anchoredPosition != position)
            rectTransform.anchoredPosition = position;
    }
}

}
