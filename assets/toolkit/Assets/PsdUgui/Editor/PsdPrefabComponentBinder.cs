using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PsdUgui.Editor
{
    internal static class PsdPrefabComponentBinder
    {
        public static void Bind(string prefabPath)
        {
            var root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                BindHierarchy(root);
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath, out bool success);
                if (!success)
                    throw new IOException($"保存预制体失败：{prefabPath}");
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        internal static void BindHierarchy(GameObject root)
        {
            // 使用快照，避免配置 ScrollRect 层级时影响遍历。
            foreach (var rect in root.GetComponentsInChildren<RectTransform>(true))
            {
                GameObject node = rect.gameObject;
                string name = node.name;
                if (string.Equals(name, "top", StringComparison.OrdinalIgnoreCase))
                    node.GetOrAddComponent<TopAreaAdapter>();
                else if (HasPrefix(name, "btn_"))
                {
                    var button = node.GetOrAddComponent<Button>();
                    button.targetGraphic = EnsureHitGraphic(node);
                }
                else if (HasPrefix(name, "tog_"))
                    BindToggle(node);
                else if (HasPrefix(name, "input_"))
                    BindInput(node);
                else if (HasPrefix(name, "slider_"))
                    BindSlider(node);
                else if (HasPrefix(name, "scrollH_"))
                    BindScroll(rect, true);
                else if (HasPrefix(name, "scrollV_"))
                    BindScroll(rect, false);
            }
        }

        private static void BindToggle(GameObject node)
        {
            var selected = FindPart(node.transform, "_on", true);
            var unselected = FindPart(node.transform, "_off", true);
            if (selected.IsChildOf(unselected) || unselected.IsChildOf(selected))
                throw new InvalidDataException($"{NodePath(node.transform)}：_on 和 _off 不能互相嵌套。");
            var toggle = node.GetOrAddComponent<Toggle>();
            toggle.targetGraphic = EnsureHitGraphic(node);
            toggle.graphic = selected.GetComponent<Graphic>();
            toggle.toggleTransition = Toggle.ToggleTransition.None;
            node.GetOrAddComponent<ToggleStateAdapter>().SetTargets(selected.gameObject, unselected.gameObject);
        }

        private static void BindInput(GameObject node)
        {
            var value = RequireComponent<Text>(FindPart(node.transform, "_on", true));
            var placeholder = RequireComponent<Text>(FindPart(node.transform, "_off", true));
            var input = node.GetOrAddComponent<InputField>();
            input.targetGraphic = EnsureHitGraphic(node);
            value.gameObject.SetActive(true);
            value.raycastTarget = false;
            placeholder.raycastTarget = false;
            input.textComponent = value;
            input.placeholder = placeholder;
            input.ForceLabelUpdate();
        }

        private static void BindSlider(GameObject node)
        {
            var background = RequireComponent<Image>(FindPart(node.transform, "_back", true));
            var fill = RequireComponent<Image>(FindPart(node.transform, "_front", true));
            var handle = RequireComponent<Image>(FindPart(node.transform, "_move", true));
            var slider = node.GetOrAddComponent<Slider>();
            EnsureHitGraphic(node);
            background.raycastTarget = false;
            fill.raycastTarget = false;
            handle.raycastTarget = true;
            slider.fillRect = fill.rectTransform;
            slider.handleRect = handle.rectTransform;
            slider.targetGraphic = handle;
        }

        private static void BindScroll(RectTransform root, bool horizontal)
        {
            var viewport = RequireComponent<RectTransform>(FindPart(root, "viewport", false));
            var content = RequireComponent<RectTransform>(FindPart(root, "content", false));
            if (viewport.IsChildOf(content))
                throw new InvalidDataException($"{NodePath(root)}：viewport 不能位于 content 内。");
            if (viewport.parent != root)
                viewport.SetParent(root, false);
            if (content.parent != viewport)
                content.SetParent(viewport, false);

            viewport.anchorMin = Vector2.zero;
            viewport.anchorMax = Vector2.one;
            viewport.pivot = new Vector2(0.5f, 0.5f);
            viewport.offsetMin = viewport.offsetMax = Vector2.zero;
            viewport.gameObject.GetOrAddComponent<RectMask2D>();
            EnsureHitGraphic(viewport.gameObject);

            var scroll = root.gameObject.GetOrAddComponent<ScrollRect>();
            scroll.viewport = viewport;
            scroll.content = content;
            scroll.horizontal = horizontal;
            scroll.vertical = !horizontal;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            content.anchorMin = horizontal ? Vector2.zero : new Vector2(0f, 1f);
            content.anchorMax = horizontal ? new Vector2(0f, 1f) : Vector2.one;
            content.pivot = horizontal ? new Vector2(0f, 0.5f) : new Vector2(0.5f, 1f);
            content.anchoredPosition = Vector2.zero;
            content.sizeDelta = horizontal ? new Vector2(content.sizeDelta.x, 0f) : new Vector2(0f, content.sizeDelta.y);

            // 同一个 content 只保留当前方向的布局组件。
            foreach (var existing in content.GetComponents<LayoutGroup>())
            {
                if (horizontal ? !(existing is HorizontalLayoutGroup) : !(existing is VerticalLayoutGroup))
                    UnityEngine.Object.DestroyImmediate(existing);
            }
            HorizontalOrVerticalLayoutGroup layout = horizontal
                ? (HorizontalOrVerticalLayoutGroup)content.gameObject.GetOrAddComponent<HorizontalLayoutGroup>()
                : content.gameObject.GetOrAddComponent<VerticalLayoutGroup>();
            layout.childAlignment = horizontal ? TextAnchor.MiddleLeft : TextAnchor.UpperCenter;
            layout.childForceExpandWidth = layout.childForceExpandHeight = false;
            // PSD 已提供子节点尺寸，不用布局组件重算宽高。
            layout.childControlWidth = layout.childControlHeight = false;
            var fitter = content.gameObject.GetOrAddComponent<ContentSizeFitter>();
            fitter.horizontalFit = horizontal ? ContentSizeFitter.FitMode.PreferredSize : ContentSizeFitter.FitMode.Unconstrained;
            fitter.verticalFit = horizontal ? ContentSizeFitter.FitMode.Unconstrained : ContentSizeFitter.FitMode.PreferredSize;
        }

        private static Graphic EnsureHitGraphic(GameObject node)
        {
            var graphic = node.GetComponent<Graphic>();
            if (graphic == null)
            {
                graphic = node.GetOrAddComponent<Image>();
                graphic.color = Color.clear;
            }
            // 导出时无切图的 Image 默认被禁用，改为透明点击区域。
            if (!graphic.enabled && graphic is Image image && image.sprite == null)
                graphic.color = Color.clear;
            graphic.enabled = true;
            graphic.raycastTarget = true;
            return graphic;
        }

        private static T RequireComponent<T>(Transform node) where T : Component
        {
            var component = node.GetComponent<T>();
            if (component == null)
                throw new InvalidDataException($"{NodePath(node)}：缺少 {typeof(T).Name} 组件，请检查 PSD 图层类型。");
            return component;
        }

        private static Transform FindPart(Transform root, string pattern, bool suffix)
        {
            var matches = new List<Transform>();
            CollectParts(root, pattern, suffix, matches);
            if (matches.Count != 1)
                throw new InvalidDataException($"{NodePath(root)}：需要唯一的{(suffix ? "名称后缀为" : "名称为")} {pattern} 的子节点，实际找到 {matches.Count} 个。");
            return matches[0];
        }

        private static void CollectParts(Transform root, string pattern, bool suffix, List<Transform> matches)
        {
            foreach (Transform child in root)
            {
                if (suffix ? child.name.EndsWith(pattern, StringComparison.OrdinalIgnoreCase)
                    : string.Equals(child.name, pattern, StringComparison.OrdinalIgnoreCase))
                    matches.Add(child);
                // 嵌套控件的内部节点由该控件自己绑定。
                if (!IsControl(child.name))
                    CollectParts(child, pattern, suffix, matches);
            }
        }

        private static bool IsControl(string name)
        {
            return HasPrefix(name, "btn_") || HasPrefix(name, "tog_") || HasPrefix(name, "input_") ||
                HasPrefix(name, "slider_") || HasPrefix(name, "scrollH_") || HasPrefix(name, "scrollV_");
        }

        private static bool HasPrefix(string name, string prefix)
        {
            return name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string NodePath(Transform node)
        {
            return node.parent == null ? node.name : NodePath(node.parent) + "/" + node.name;
        }
    }
}
