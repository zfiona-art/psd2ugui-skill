using System;
using System.Reflection;
using PsdUgui;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI绑定代码生成器 - 自动绑定模块
/// </summary>
namespace PsdUgui.Editor
{
public static partial class UIBindCodeGenerator
{
    /// <summary>
    /// 组件绑定器：负责通过反射将 SerializeField 字段与预制体节点绑定
    /// </summary>
    private static class Binding
    {
        /// <summary>
        /// 自动绑定：遍历脚本的 SerializeField 字段，查找同名节点并赋值
        /// </summary>
        public static void AutoBindNode(GameObject root, PsdUguiViewBase uiBase)
        {
            var type = uiBase.GetType();
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            
            bool anyBound = false;
            int bindCount = 0;

            foreach (var field in fields)
            {
                if (!Attribute.IsDefined(field, typeof(SerializeField)))
                    continue;

                var target = FindChildByFieldName(root.transform, field.Name);
                if (target == null)
                {
                    Debug.LogError($"AutoBind: 找不到节点 {field.Name}");
                    continue;
                }

                if (TryBindField(field, target, uiBase))
                {
                    anyBound = true;
                    bindCount++;
                }
            }

            // 绑定 item 组件内部的字段
            foreach (var field in fields)
            {
                if (!Attribute.IsDefined(field, typeof(SerializeField)))
                    continue;

                // 检查是否是 item 类型字段（字段名以 item 开头）（继承 MonoBehaviour 但不是 PsdUguiViewBase）
                var fieldType = field.FieldType;
                if (field.Name.StartsWith("item") && typeof(MonoBehaviour).IsAssignableFrom(fieldType) && !typeof(PsdUguiViewBase).IsAssignableFrom(fieldType))
                {
                    var itemComponent = field.GetValue(uiBase) as MonoBehaviour;
                    if (itemComponent != null)
                    {
                        var itemNode = itemComponent.gameObject;
                        if (AutoBindItemFields(itemComponent, itemNode))
                        {
                            anyBound = true;
                        }
                    }
                }
            }

            if (anyBound)
            {
                // 标记组件为 dirty，确保字段修改能被正确序列化保存
                EditorUtility.SetDirty(uiBase);
                AssetDatabase.SaveAssets();
                Debug.Log($"AutoBind 完成并保存预制体: {root.name}，绑定了 {bindCount} 个字段");
            }
            else
            {
                Debug.Log("AutoBind: 没有需要绑定的字段");
            }
        }

        /// <summary>
        /// 绑定 item 组件内部的字段
        /// </summary>
        private static bool AutoBindItemFields(MonoBehaviour itemComponent, GameObject itemRoot)
        {
            var type = itemComponent.GetType();
            var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

            bool anyBound = false;
            int bindCount = 0;
            
            foreach (var field in fields)
            {
                if (!Attribute.IsDefined(field, typeof(SerializeField)))
                    continue;

                var target = FindChildByFieldName(itemRoot.transform, field.Name);
                if (target == null)
                {
                    Debug.LogError($"AutoBindItem: 找不到节点 {field.Name}");
                    continue;
                }

                if (TryBindItemField(field, target, itemComponent))
                {
                    bindCount++;
                    anyBound = true;
                }
            }

            if (anyBound)
            {
                Debug.Log($"AutoBind 完成并保存Item: {itemRoot.name}，绑定了 {bindCount} 个字段");
                EditorUtility.SetDirty(itemComponent);
                AssetDatabase.SaveAssets();
            }

            return anyBound;
        }

        // ───────────────────────────────────────────────
        //  字段绑定
        // ───────────────────────────────────────────────

        /// <summary>
        /// 尝试将字段绑定到目标 Transform
        /// </summary>
        private static bool TryBindField(FieldInfo field, Transform target, PsdUguiViewBase uiBase)
        {
            var fieldType = field.FieldType;
            object value = null;

            if (fieldType == typeof(GameObject))
            {
                value = target.gameObject;
            }
            else if (fieldType == typeof(Transform))
            {
                value = target;
            }
            else if (fieldType == typeof(RectTransform))
            {
                value = target as RectTransform;
            }
            else if (typeof(Component).IsAssignableFrom(fieldType))
            {
                // 如果是 MonoBehaviour 子类且不是 PsdUguiViewBase，可能是 item 类型，需要自动附加组件
                if (field.Name.StartsWith("item") && fieldType.IsSubclassOf(typeof(MonoBehaviour)) && !fieldType.IsSubclassOf(typeof(PsdUguiViewBase)))
                {
                    var comp = target.GetComponent(fieldType);
                    if (comp == null)
                    {
                        comp = target.gameObject.AddComponent(fieldType);
                        Debug.Log($"AutoBind: {uiBase.name} 自动附加组件 {fieldType.Name} 到 {target.name}");
                    }
                    value = comp;
                }
                else
                {
                    var comp = target.GetComponent(fieldType);
                    if (comp == null)
                    {
                        Debug.LogError($"AutoBind: {uiBase.name} 节点 {target.name} 上缺少组件 {fieldType.Name}绑定失败");
                        return false;
                    }
                    value = comp;
                }
            }
            else
            {
                Debug.LogError($"AutoBind: {uiBase.name} 不支持的字段类型 {fieldType.Name}，字段 {field.Name}");
                return false;
            }

            field.SetValue(uiBase, value);
            return true;
        }

        /// <summary>
        /// 尝试将 item 字段绑定到目标 Transform
        /// </summary>
        private static bool TryBindItemField(FieldInfo field, Transform target, MonoBehaviour itemComponent)
        {
            var fieldType = field.FieldType;
            object value = null;

            if (fieldType == typeof(GameObject))
            {
                value = target.gameObject;
            }
            else if (fieldType == typeof(Transform))
            {
                value = target;
            }
            else if (fieldType == typeof(RectTransform))
            {
                value = target as RectTransform;
            }
            else if (typeof(Component).IsAssignableFrom(fieldType))
            {
                var comp = target.GetComponent(fieldType);
                if (comp == null)
                {
                    Debug.LogError($"AutoBindItem: 节点 {target.name} 上缺少组件 {fieldType.Name}，字段 {field.Name} 绑定失败");
                    return false;
                }
                value = comp;
            }
            else
            {
                Debug.LogError($"AutoBindItem: 不支持的字段类型 {fieldType.Name}，字段 {field.Name}");
                return false;
            }

            field.SetValue(itemComponent, value);
            return true;
        }
    }
}

}
