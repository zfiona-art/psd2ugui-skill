using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using PsdUgui;
using UnityEditor;
using UnityEditor.Callbacks;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// UI绑定代码生成器 - 主入口与流程编排
/// </summary>
namespace PsdUgui.Editor
{
public static partial class UIBindCodeGenerator
{
    #region 数据模型

    /// <summary>
    /// 收集结果：包含主脚本节点和 item 节点
    /// </summary>
    public class CollectedNodes
    {
        public List<(string name, Type componentType)> MainNodes = new List<(string name, Type componentType)>();
        public List<ItemNodeInfo> ItemNodes = new List<ItemNodeInfo>();
    }

    /// <summary>
    /// item 节点信息
    /// </summary>
    public class ItemNodeInfo
    {
        public string NodeName;      // 原始节点名，如 "itemShop"
        public string ScriptName;    // 脚本名，如 "ShopItem"
        public List<(string name, Type componentType)> ChildNodes = new List<(string name, Type componentType)>();
    }

    #endregion

    #region 配置与常量

    /// <summary>前缀到组件类型的映射表</summary>
    private static readonly Dictionary<string, Type> PrefixComponentMap = new Dictionary<string, Type>
    {
        { "btn",    typeof(Button) },
        { "txt",    typeof(Text) },
        { "slider", typeof(Slider) },
        { "input",  typeof(InputField) },
        { "tog",    typeof(Toggle) },
        { "img",    typeof(Image) },
        { "rect",   typeof(RectTransform) },
        { "scroll", typeof(ScrollRect) },
    };

    /// <summary>
    /// 根据 item 节点名获取脚本名：itemShop -> ShopItem
    /// </summary>
    private static string GetItemScriptName(string nodeName)
    {
        if (nodeName.StartsWith("item"))
        {
            var namePart = nodeName.Substring(4);
            if (!string.IsNullOrEmpty(namePart))
            {
                return char.ToUpper(namePart[0]) + namePart.Substring(1) + "Item";
            }
        }
        return nodeName + "Item";
    }

    private const string PendingMainUIKey  = "PsdUgui_PendingMainUIPath";
    private const string PendingNodeUIKey  = "PsdUgui_PendingNodeUIPath";

    #endregion

    // ───────────────────────────────────────────────
    //  入口
    // ───────────────────────────────────────────────

    [MenuItem("GameObject/PSD UGUI/Generate UI Code", false, 0)]
    [MenuItem("Assets/PSD UGUI/Generate UI Code", false, 0)]
    private static void GenerateMainCode()
    {
        GenerateMainCode(Selection.activeObject as GameObject);
    }

    /// <summary>复用菜单流程，批量生成后统一触发编译。</summary>
    public static void GenerateForPrefabs(IEnumerable<GameObject> prefabs)
    {
        WithDeferredRefresh(() =>
        {
            foreach (var prefab in prefabs)
                GenerateMainCode(prefab);
        });
    }

    private static int deferRefreshDepth;
    private static bool refreshRequested;

    private static void WithDeferredRefresh(Action action)
    {
        deferRefreshDepth++;
        try
        {
            action();
        }
        finally
        {
            deferRefreshDepth--;
            if (deferRefreshDepth == 0 && refreshRequested)
            {
                refreshRequested = false;
                AssetDatabase.Refresh();
            }
        }
    }

    private static void RequestScriptRefresh()
    {
        if (deferRefreshDepth > 0)
            refreshRequested = true;
        else
            AssetDatabase.Refresh();
    }

    private static void GenerateMainCode(GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("请选择一个 Prefab");
            return;
        }

        var className      = prefab.name;
        var viewDir        = "Assets/Scripts/View";
        var mainScriptPath = Path.Combine(viewDir, $"{className}.cs");

        // ── 生成骨架脚本 ──
        if (!File.Exists(mainScriptPath))
        {
            if (!Directory.Exists(viewDir))
                Directory.CreateDirectory(viewDir);

            var collected = PrefabAnalyzer.CollectAllNodes(prefab);
            CodeGen.GenerateItemScripts(collected);
            CodeGen.GenerateSkeletonScript(className, mainScriptPath, collected);
            Debug.Log($"生成主体代码: {mainScriptPath}");
        }

        // ── 加载并编译脚本 ──
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(mainScriptPath);
        if (script == null || script.GetClass() == null)
        {
            // 脚本未编译完成，注册回调等待
            QueuePendingPrefab(PendingMainUIKey, prefab);
            RequestScriptRefresh();
            return;
        }

        // 脚本已编译，直接处理
        AttachAndGenerate(prefab, script.GetClass());
    }

    // ───────────────────────────────────────────────
    //  脚本编译回调
    // ───────────────────────────────────────────────

    [DidReloadScripts]
    private static void OnScriptsReloaded()
    {
        // 延迟至资源数据库完成重载，再加载预制体并写入绑定。
        EditorApplication.delayCall += ProcessPendingPrefabs;
    }

    private static void ProcessPendingPrefabs()
    {
        var mainPaths = TakePendingPrefabs(PendingMainUIKey);
        var nodePaths = TakePendingPrefabs(PendingNodeUIKey);
        WithDeferredRefresh(() =>
        {
            foreach (var path in mainPaths)
            {
                try { TryHandlePendingMainPrefab(path); }
                catch (Exception error) { Debug.LogException(error); }
            }
            foreach (var path in nodePaths)
            {
                try { TryHandlePendingNodePrefab(path); }
                catch (Exception error) { Debug.LogException(error); }
            }
        });
    }

    #region 回调分发

    private static void TryHandlePendingNodePrefab(string path)
    {
        Debug.Log("代码编译完成");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"无法加载 Prefab: {path}");
            return;
        }
        var uiBase = prefab.GetComponent<PsdUguiViewBase>();
        if (uiBase == null)
        {
            Debug.LogError($"无法加载 PsdUguiViewBase: {prefab.name}");
            return;
        }
        Binding.AutoBindNode(prefab, uiBase);
    }
    
    private static void TryHandlePendingMainPrefab(string path)
    {
        Debug.Log("代码编译完成");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            Debug.LogError($"无法加载 Prefab: {path}");
            return;
        }

        var className      = prefab.name;
        var viewDir        = "Assets/Scripts/View";
        var mainScriptPath = Path.Combine(viewDir, $"{className}.cs");
        var script = AssetDatabase.LoadAssetAtPath<MonoScript>(mainScriptPath);
        if (script == null)
        {
            Debug.LogError($"无法加载脚本: {mainScriptPath}");
            return;
        }

        var componentType = script.GetClass();
        if (componentType == null)
        {
            Debug.LogError($"脚本 {className} 编译后仍无法获取类型，请检查脚本是否有编译错误");
            return;
        }
        
        AttachAndGenerate(prefab, componentType);
    }

    #endregion

    // ───────────────────────────────────────────────
    //  流程编排
    // ───────────────────────────────────────────────

    private static void AttachAndGenerate(GameObject prefab, Type componentType)
    {
        if (!typeof(PsdUguiViewBase).IsAssignableFrom(componentType))
            throw new InvalidOperationException($"{componentType.Name} 必须继承 PsdUguiViewBase。");

        // 附加组件到预制体
        if (prefab.GetComponent(componentType) == null)
        {
            prefab.AddComponent(componentType);
            EditorUtility.SetDirty(prefab);
            AssetDatabase.SaveAssets();
            Debug.Log($"已将 {componentType.Name} 脚本附加到预制体");
        }
        
        var uiBase = prefab.GetComponent(componentType) as PsdUguiViewBase;
        if (uiBase == null)
        {
            Debug.LogError($"无法加载 PsdUguiViewBase: {prefab.name}");
            return;
        }
        ReGenerate(prefab, uiBase);
    }

    // ───────────────────────────────────────────────
    //  外部接口
    // ───────────────────────────────────────────────

    /// <summary>
    /// 重新生成组件代码并等待编译完成后自动绑定
    /// </summary>
    public static void ReGenerate(GameObject root, PsdUguiViewBase uiBase)
    {
        var hasChanged = CodeGen.GenerateNode(root, uiBase);
        if (!hasChanged)
        {
            Binding.AutoBindNode(root, uiBase);
        }
    }
}

}
