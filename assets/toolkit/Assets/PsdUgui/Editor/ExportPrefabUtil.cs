using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace PsdUgui.Editor
{
    internal static class ExportPrefabUtil
    {
        [Serializable]
        private sealed class Layout
        {
            public string name;
            public bool isHidden;
            public float width;
            public float height;
            public Node[] children;
        }

        [Serializable]
        private sealed class Node
        {
            public string name;
            public string type;
            public float x;
            public float y;
            public float w;
            public float h;
            public string sprite;
            public bool isHidden;
            public string text;
            public float fontSize;
            public string fontName;
            public string color;
            public OutlineInfo outline;
            public Node[] children;
        }

        [Serializable]
        private sealed class OutlineInfo
        {
            public float size;
            public string color;
            public float alpha;
        }

        [Serializable]
        private sealed class PageManifest
        {
            public string[] pages;
        }

        public static string[] GetPageNames(string uiRelativePath, string sourceName)
        {
            string manifestPath = Path.Combine(Application.dataPath, uiRelativePath, "Config", "psd", "Manifests", sourceName + ".json");
            if (!File.Exists(manifestPath))
                return new[] { sourceName };
            var manifest = JsonUtility.FromJson<PageManifest>(File.ReadAllText(manifestPath, Encoding.UTF8));
            if (manifest == null || manifest.pages == null)
                throw new InvalidDataException($"画板清单无效：{manifestPath}");
            return manifest.pages;
        }

        internal static string GetPrefabAssetPath(string uiRelativePath, string pageName)
        {
            return $"{ToAssetPath(uiRelativePath)}/Prefabs/UI/{SanitizeFileName(pageName)}.prefab";
        }

        public static string Export(string uiRelativePath, string pageName, out string[] missingImages, float lineSpacing = 1f)
        {
            string uiFolder = ToAssetPath(uiRelativePath);
            string jsonAssetPath = $"{uiFolder}/Config/psd/{pageName}.json";
            string jsonFullPath = Path.Combine(Application.dataPath, uiRelativePath, "Config", "psd", pageName + ".json");

            if (!File.Exists(jsonFullPath))
                throw new FileNotFoundException($"找不到布局 JSON：{jsonAssetPath}。请先导出 UI。", jsonFullPath);

            var layout = JsonUtility.FromJson<Layout>(File.ReadAllText(jsonFullPath, Encoding.UTF8));
            if (layout == null || layout.width <= 0 || layout.height <= 0)
                throw new InvalidDataException($"布局 JSON 缺少有效画布尺寸：{jsonAssetPath}");

            var sprites = new Dictionary<string, Sprite>(StringComparer.Ordinal);
            var missing = new List<string>();
            CollectSprites(layout.children, uiFolder, sprites, missing);
            missingImages = new HashSet<string>(missing, StringComparer.Ordinal).ToArray();
            missing.Clear();

            var fontNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            CollectFontNames(layout.children, fontNames, missing);
            var fonts = new Dictionary<string, Font>(StringComparer.OrdinalIgnoreCase);
            foreach (string fontName in fontNames)
            {
                Font font = FindProjectFont(fontName);
                if (font == null)
                    missing.Add(fontName);
                else
                    fonts.Add(fontName, font);
            }
            if (missing.Count > 0)
                throw new InvalidDataException("布局字体在 Assets 中找不到对应的 Font 资源：\n" +
                    string.Join("\n", missing) + "\n请将对应的 .ttf 或 .otf 字体导入项目后重试。");

            string rootName = pageName;
            string prefabAssetPath = GetPrefabAssetPath(uiRelativePath, pageName);
            string prefabFullFolder = Path.Combine(Application.dataPath, uiRelativePath, "Prefabs", "UI");
            Directory.CreateDirectory(prefabFullFolder);
            AssetDatabase.Refresh();

            var root = new GameObject(rootName, typeof(RectTransform));
            try
            {
                var rootRect = (RectTransform)root.transform;
                rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
                rootRect.pivot = new Vector2(0.5f, 0.5f);
                rootRect.sizeDelta = new Vector2(layout.width, layout.height);

                if (layout.children != null)
                {
                    // 导出的图层列表是前景在前；UGUI 后面的兄弟节点绘制在前景。
                    for (int i = layout.children.Length - 1; i >= 0; i--)
                        AddNode(layout.children[i], rootRect, 0, 0, fonts, sprites, lineSpacing);
                }

                rootRect.anchorMin = Vector2.zero;
                rootRect.anchorMax = Vector2.one;
                rootRect.anchoredPosition = Vector2.zero;
                rootRect.sizeDelta = Vector2.zero;

                root.SetActive(!layout.isHidden);
                PrefabUtility.SaveAsPrefabAsset(root, prefabAssetPath, out bool success);
                if (!success)
                    throw new IOException($"保存预制体失败：{prefabAssetPath}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }

            return prefabAssetPath;
        }

        private static void CollectSprites(Node[] nodes, string uiFolder,
            Dictionary<string, Sprite> sprites, List<string> missing)
        {
            if (nodes == null)
                return;

            foreach (Node node in nodes)
            {
                if (node == null)
                    continue;

                if (string.Equals(node.type, "image", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(node.sprite) ||
                        Path.GetFileName(node.sprite) != node.sprite ||
                        node.sprite.Contains("\\"))
                    {
                        missing.Add(node.name);
                    }
                    else if (!sprites.ContainsKey(node.sprite))
                    {
                        string folder = Path.GetFileNameWithoutExtension(node.sprite).EndsWith("_bg", StringComparison.OrdinalIgnoreCase)
                            ? "Big" : "Main";
                        string assetPath = $"{uiFolder}/Textures/{folder}/{node.sprite}";
                        Sprite sprite = LoadSprite(assetPath);
                        if (sprite == null)
                            missing.Add(node.sprite);
                        else
                            sprites.Add(node.sprite, sprite);
                    }
                }

                CollectSprites(node.children, uiFolder, sprites, missing);
            }
        }

        private static Sprite LoadSprite(string assetPath)
        {
            Sprite sprite = AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
            if (sprite != null)
                return sprite;
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null)
                return null;
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Sprite>(assetPath);
        }

        private static void CollectFontNames(Node[] nodes, HashSet<string> names, List<string> missing)
        {
            if (nodes == null)
                return;

            foreach (Node node in nodes)
            {
                if (node == null)
                    continue;

                if (string.Equals(node.type, "text", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrWhiteSpace(node.fontName))
                        missing.Add($"{node.name}: fontName 为空");
                    else
                        names.Add(node.fontName.Trim());
                }
                CollectFontNames(node.children, names, missing);
            }
        }

        private static Font FindProjectFont(string fontName)
        {
            string[] guids = AssetDatabase.FindAssets("t:Font", new[] { "Assets" });
            foreach (string guid in guids)
            {
                string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                Font font = AssetDatabase.LoadAssetAtPath<Font>(assetPath);
                if (font == null)
                    continue;

                if (string.Equals(Path.GetFileNameWithoutExtension(assetPath), fontName, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(font.name, fontName, StringComparison.OrdinalIgnoreCase))
                    return font;

                foreach (string faceName in font.fontNames ?? Array.Empty<string>())
                {
                    if (string.Equals(faceName, fontName, StringComparison.OrdinalIgnoreCase))
                        return font;
                }
            }
            return null;
        }

        private static void AddNode(Node node, RectTransform parent, float parentX, float parentY,
            Dictionary<string, Font> fonts, Dictionary<string, Sprite> sprites, float lineSpacing)
        {
            if (node == null)
                return;

            var gameObject = new GameObject(string.IsNullOrEmpty(node.name) ? "unnamed" : node.name,
                typeof(RectTransform));
            var rect = (RectTransform)gameObject.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0, 1);
            rect.pivot = new Vector2(0, 1);
            rect.anchoredPosition = new Vector2(node.x - parentX, -(node.y - parentY));
            rect.sizeDelta = new Vector2(node.w, node.h);

            if (string.Equals(node.type, "image", StringComparison.OrdinalIgnoreCase))
            {
                var image = gameObject.AddComponent<Image>();
                if (!string.IsNullOrEmpty(node.sprite) && sprites.TryGetValue(node.sprite, out Sprite sprite))
                    image.sprite = sprite;
                else
                    image.enabled = false;
                bool isNineSlice = !string.IsNullOrEmpty(node.sprite) &&
                    Path.GetFileNameWithoutExtension(node.sprite).EndsWith("_9s", StringComparison.OrdinalIgnoreCase);
                image.type = isNineSlice || (image.sprite != null && image.sprite.border.sqrMagnitude > 0)
                    ? Image.Type.Sliced : Image.Type.Simple;
                image.raycastTarget = false;
            }
            else if (string.Equals(node.type, "text", StringComparison.OrdinalIgnoreCase))
            {
                var text = gameObject.AddComponent<Text>();
                text.font = fonts[node.fontName.Trim()];
                text.text = node.text ?? string.Empty;
                text.fontSize = Mathf.Max(1, Mathf.RoundToInt(node.fontSize));
                text.color = ParseColor(node.color, Color.white);
                bool isMultiline = text.text.Contains("\n") || text.text.Contains("\r");
                text.alignment = isMultiline ? TextAnchor.UpperLeft : TextAnchor.MiddleCenter;
                text.lineSpacing = isMultiline ? lineSpacing : 1f;
                // Measure at the requested font size before enabling Best Fit.
                text.resizeTextForBestFit = false;
                var settings = text.GetGenerationSettings(Vector2.zero);
                settings.horizontalOverflow = HorizontalWrapMode.Overflow;
                settings.verticalOverflow = VerticalWrapMode.Overflow;
                settings.resizeTextForBestFit = false;
                var generator = new TextGenerator();
                var lines = text.text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
                float width = 0;
                foreach (string line in lines)
                    width = Mathf.Max(width, generator.GetPreferredWidth(line, settings) / text.pixelsPerUnit);
                float height = generator.GetPreferredHeight(string.Join("\n", lines), settings) / text.pixelsPerUnit;
                float fittedWidth = Mathf.Max(node.w, Mathf.Max(1, Mathf.Ceil(width)));
                float fittedHeight = Mathf.Max(1, Mathf.Ceil(height));
                rect.sizeDelta = new Vector2(fittedWidth, fittedHeight);
                // Keep the original left-center position for single-line text;
                // resize width rightward and height symmetrically.
                if (!isMultiline)
                    rect.anchoredPosition += new Vector2(0, (fittedHeight - node.h) / 2);
                rect.anchoredPosition += new Vector2(0, text.fontSize / 10f);

                text.horizontalOverflow = HorizontalWrapMode.Wrap;
                text.verticalOverflow = VerticalWrapMode.Truncate;
                text.resizeTextForBestFit = true;
                text.resizeTextMaxSize = text.fontSize;
                text.resizeTextMinSize = Mathf.Min(text.resizeTextMinSize, text.fontSize);
                text.raycastTarget = false;

                if (node.outline != null && node.outline.size > 0)
                {
                    var outline = gameObject.AddComponent<Outline>();
                    Color outlineColor = ParseColor(node.outline.color, Color.black);
                    outlineColor.a = Mathf.Clamp01(node.outline.alpha);
                    outline.effectColor = outlineColor;
                    outline.effectDistance = new Vector2(node.outline.size, -node.outline.size);
                }
            }

            gameObject.SetActive(!node.isHidden);
            if (node.children != null)
            {
                for (int i = node.children.Length - 1; i >= 0; i--)
                    AddNode(node.children[i], rect, node.x, node.y, fonts, sprites, lineSpacing);
            }

            // 子节点布局完成后转换，保留 PSD 位置及文字尺寸调整结果。
            Vector2 anchor = GetNodeAnchor(node.name);
            Vector2 center = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition += Vector2.Scale(center - rect.pivot, rect.rect.size) +
                Vector2.Scale(rect.anchorMin - anchor, parent.rect.size);
            rect.pivot = center;
            rect.anchorMin = rect.anchorMax = anchor;
        }

        private static Vector2 GetNodeAnchor(string nodeName)
        {
            switch (nodeName?.ToLowerInvariant())
            {
                case "top": return new Vector2(0.5f, 1f);
                case "bottom": return new Vector2(0.5f, 0f);
                case "left": return new Vector2(0f, 0.5f);
                case "right": return new Vector2(1f, 0.5f);
                default: return new Vector2(0.5f, 0.5f);
            }
        }

        private static Color ParseColor(string value, Color fallback)
        {
            return !string.IsNullOrEmpty(value) && ColorUtility.TryParseHtmlString(value, out Color color)
                ? color : fallback;
        }

        private static string ToAssetPath(string relativePath)
        {
            return "Assets/" + relativePath.Trim().Replace('\\', '/');
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars())
                value = value.Replace(invalid, '_');
            return value.Replace('/', '_').Replace('\\', '_');
        }
    }
}
