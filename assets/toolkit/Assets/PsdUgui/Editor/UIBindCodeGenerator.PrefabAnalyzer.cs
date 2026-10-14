using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// UI绑定代码生成器 - Prefab 分析模块
/// </summary>
namespace PsdUgui.Editor
{
public static partial class UIBindCodeGenerator
{
    /// <summary>
    /// Prefab 分析器：负责从预制体中收集匹配前缀规则的节点
    /// </summary>
    private static class PrefabAnalyzer
    {
        /// <summary>
        /// 遍历预制体子节点，收集匹配前缀规则的节点
        /// item 节点及其子节点不参与主脚本匹配
        /// </summary>
        public static CollectedNodes CollectAllNodes(GameObject root)
        {
            var result = new CollectedNodes();
            var itemTransforms = new HashSet<Transform>();

            // 先找出所有 item 节点
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
            {
                if (child == root.transform) continue;
                if (child.name.StartsWith("item"))
                {
                    itemTransforms.Add(child);
                }
            }

            // 收集 item 节点信息
            foreach (var itemT in itemTransforms)
            {
                var itemInfo = new ItemNodeInfo
                {
                    NodeName = itemT.name,
                    ScriptName = GetItemScriptName(itemT.name),
                };

                // 递归收集 item 下的子节点（遇到嵌套 item 跳过）
                CollectItemChildren(itemT, itemT, itemInfo.ChildNodes);

                // 排序
                itemInfo.ChildNodes.Sort((a, b) =>
                {
                    int cmp = string.Compare(a.componentType.Name, b.componentType.Name, StringComparison.Ordinal);
                    if (cmp != 0) return cmp;
                    return string.Compare(a.name, b.name, StringComparison.Ordinal);
                });

                result.ItemNodes.Add(itemInfo);
            }

            // 收集主脚本节点（跳过 item 节点及其子节点）
            CollectMainNodes(root.transform, root.transform, itemTransforms, result.MainNodes);

            // 排序
            result.MainNodes.Sort((a, b) =>
            {
                int cmp = string.Compare(a.componentType.Name, b.componentType.Name, StringComparison.Ordinal);
                if (cmp != 0) return cmp;
                return string.Compare(a.name, b.name, StringComparison.Ordinal);
            });

            return result;
        }

        /// <summary>
        /// 递归收集 item 节点下的子节点，遇到嵌套 item 跳过
        /// </summary>
        private static void CollectItemChildren(Transform itemRoot, Transform current, List<(string name, Type componentType)> nodes)
        {
            foreach (Transform child in current)
            {
                // 跳过嵌套的 item 节点及其子树
                if (child != itemRoot && child.name.StartsWith("item"))
                    continue;

                // 检查当前节点是否匹配前缀
                string childName = child.name;
                foreach (var kvp in PrefixComponentMap)
                {
                    if (childName.StartsWith(kvp.Key))
                    {
                        nodes.Add((childName, kvp.Value));
                        break;
                    }
                }

                // 递归收集子节点
                CollectItemChildren(itemRoot, child, nodes);
            }
        }

        /// <summary>
        /// 递归收集主脚本节点，跳过 item 节点及其子树
        /// </summary>
        private static void CollectMainNodes(Transform root, Transform current, HashSet<Transform> itemTransforms, List<(string name, Type componentType)> nodes)
        {
            foreach (Transform child in current)
            {
                if (child == root) continue;

                // 遇到 item 节点，跳过整个子树
                if (itemTransforms.Contains(child))
                    continue;

                // 检查当前节点是否匹配前缀
                string childName = child.name;
                foreach (var kvp in PrefixComponentMap)
                {
                    if (childName.StartsWith(kvp.Key))
                    {
                        nodes.Add((childName, kvp.Value));
                        break;
                    }
                }

                // 递归收集子节点
                CollectMainNodes(root, child, itemTransforms, nodes);
            }
        }
    }
}

}
