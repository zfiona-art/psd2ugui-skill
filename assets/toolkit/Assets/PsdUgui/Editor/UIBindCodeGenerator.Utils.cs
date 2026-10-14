using PsdUgui;
using UnityEditor;
using UnityEngine;

/// <summary>
/// UI绑定代码生成器 - 通用工具方法
/// </summary>
namespace PsdUgui.Editor
{
public static partial class UIBindCodeGenerator
{
    // ───────────────────────────────────────────────
    //  Transform 查找
    // ───────────────────────────────────────────────

    /// <summary>
    /// 在预制体子节点中按名称查找 Transform
    /// </summary>
    private static Transform FindChildByFieldName(Transform root, string fieldName)
    {
        foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == fieldName)
                return child;
        }
        return null;
    }

    /// <summary>
    /// 缓存prefab的路径
    /// </summary>
    private static void QueuePendingPrefab(string key, GameObject prefab)
    {
        if (prefab == null)
        {
            Debug.LogError("无法缓存预制体路径：对象为空");
            return;
        }

        var path = AssetDatabase.GetAssetPath(prefab);
        if (string.IsNullOrEmpty(path))
        {
            var stage = UnityEditor.SceneManagement.PrefabStageUtility.GetPrefabStage(prefab);
            if (stage != null)
                path = stage.assetPath;
        }
        if (string.IsNullOrEmpty(path))
            path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(prefab);

        if (string.IsNullOrEmpty(path))
        {
            var matches = new System.Collections.Generic.List<string>();
            foreach (var guid in AssetDatabase.FindAssets("t:Prefab"))
            {
                var candidatePath = AssetDatabase.GUIDToAssetPath(guid);
                if (System.IO.Path.GetFileNameWithoutExtension(candidatePath) != prefab.name)
                    continue;

                matches.Add(candidatePath);
            }

            if (matches.Count != 1)
            {
                Debug.LogError(matches.Count == 0
                    ? $"找不到名称为 {prefab.name} 的预制体，无法缓存路径"
                    : $"存在多个名称为 {prefab.name} 的预制体，无法确定路径：\n{string.Join("\n", matches)}");
                return;
            }
            path = matches[0];
        }
        Debug.Log("缓存prefab路径:" + path);
        string sessionKey = key + ":" + Application.dataPath;
        var pending = JsonUtility.FromJson<PendingPrefabPaths>(SessionState.GetString(sessionKey, ""))
            ?? new PendingPrefabPaths();
        if (!pending.paths.Contains(path))
            pending.paths.Add(path);
        SessionState.SetString(sessionKey, JsonUtility.ToJson(pending));
    }

    [System.Serializable]
    private sealed class PendingPrefabPaths
    {
        public System.Collections.Generic.List<string> paths = new System.Collections.Generic.List<string>();
    }

    private static string[] TakePendingPrefabs(string key)
    {
        string sessionKey = key + ":" + Application.dataPath;
        var pending = JsonUtility.FromJson<PendingPrefabPaths>(SessionState.GetString(sessionKey, ""));
        SessionState.EraseString(sessionKey);
        return pending == null ? System.Array.Empty<string>() : pending.paths.ToArray();
    }

    /// <summary>
    /// 通过反射类型查找对应的 MonoScript 文件路径
    /// </summary>
    private static string GetScriptPath(System.Type type)
    {
        string[] guids = AssetDatabase.FindAssets($"{type.Name} t:MonoScript");

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);

            if (script != null && script.GetClass() == type)
                return path;
        }

        return null;
    }
}

}
