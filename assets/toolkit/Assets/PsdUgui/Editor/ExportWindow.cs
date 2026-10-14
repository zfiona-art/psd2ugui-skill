using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace PsdUgui.Editor
{
    public sealed class PsdExportWindow : EditorWindow
    {
        private string sourceAddress;
        private string uiPath;
        private bool greenOnly;
        private float lineSpacing = 1f;
        private float exportLineSpacing;
        private bool exportGreenOnly;
        private string exportMode;
        private bool exporting;
        private string status;
        private Task<ExportResult> exportTask;
        private string exportPath;
        private Queue<string> sources;
        private string currentSource;
        private string batchSource;
        private readonly HashSet<string> exportedPages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly List<string> exportedPrefabPaths = new List<string>();
        private int completed;
        private int total;

        [MenuItem("Tools/PSD UGUI/Export")]
        private static void Open()
        {
            GetWindow<PsdExportWindow>("psd").minSize = new Vector2(540, 380);
        }

        private string SourceKey => $"PsdUgui.Export.Source.{Application.dataPath}";
        private string GreenKey => $"PsdUgui.Export.GreenOnly.{Application.dataPath}";
        private string LineSpacingKey => $"PsdUgui.Export.LineSpacing.{Application.dataPath}";
        private string PathKey => $"PsdUgui.Export.UiPath.{Application.dataPath}";

        private void OnEnable()
        {
            greenOnly = EditorPrefs.GetBool(GreenKey, false);
            lineSpacing = EditorPrefs.GetFloat(LineSpacingKey, 1f);
            sourceAddress = EditorPrefs.GetString(SourceKey, string.Empty);
            uiPath = NormalizeCachedPath(EditorPrefs.GetString(PathKey, string.Empty));
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("PSD 资源导出", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("输入项目根目录下的 PSD 文件或文件夹路径。文件夹会递归导出其中所有 PSD 文件。结构保存到 UI 导出路径的 Config/psd；勾选绿色标签后只导出绿色图片。", MessageType.Info);

            EditorGUI.BeginChangeCheck();
            using (new EditorGUILayout.HorizontalScope())
            {
                sourceAddress = EditorGUILayout.TextField("源地址", sourceAddress ?? string.Empty);
                using (new EditorGUI.DisabledScope(exporting))
                {
                    if (GUILayout.Button("选择文件", GUILayout.Width(80)))
                        SelectSourceFile();
                }
            }
            uiPath = EditorGUILayout.TextField("UI 导出路径", uiPath ?? string.Empty);
            greenOnly = EditorGUILayout.Toggle("是否有绿色标签", greenOnly);
            lineSpacing = EditorGUILayout.FloatField(new GUIContent("多行文字行距", "Unity Text 的 Line Spacing 倍率，默认 1.0"), lineSpacing);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetBool(GreenKey, greenOnly);
                EditorPrefs.SetFloat(LineSpacingKey, lineSpacing);
                EditorPrefs.SetString(SourceKey, sourceAddress);
                EditorPrefs.SetString(PathKey, uiPath);
            }

            string error = ValidateSource(sourceAddress) ?? ValidateDirectory(uiPath);
            if (error == null && (float.IsNaN(lineSpacing) || float.IsInfinity(lineSpacing) || lineSpacing <= 0))
                error = "多行文字行距必须为大于 0 的有限数值。";
            if (error != null)
                EditorGUILayout.HelpBox(error, MessageType.Warning);
            using (new EditorGUI.DisabledScope(exporting || EditorApplication.isCompiling || error != null))
            {
                if (GUILayout.Button("导出结构"))
                    StartExport("structure");
                if (GUILayout.Button("导出图片"))
                    StartExport("images");
                if (GUILayout.Button("导出预制体"))
                    StartExport("prefab");
                if (GUILayout.Button(new GUIContent("绑定组件", "按节点名称添加并绑定交互组件")))
                    StartExport("components");
                if (GUILayout.Button("生成脚本"))
                    StartExport("scripts");
            }
            if (!string.IsNullOrEmpty(status))
                EditorGUILayout.HelpBox(status, MessageType.None);
            GUILayout.FlexibleSpace();
            using (new EditorGUI.DisabledScope(exporting || EditorApplication.isCompiling || error != null))
            {
                if (GUILayout.Button(exporting ? "导出中..." : "一键导出"))
                    StartExport("all");
            }
        }

        private void SelectSourceFile()
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string directory = root;
            if (!string.IsNullOrWhiteSpace(sourceAddress) && ValidateSource(sourceAddress) == null)
            {
                string currentPath = Path.GetFullPath(Path.Combine(root, sourceAddress.Trim()));
                directory = Directory.Exists(currentPath) ? currentPath : Path.GetDirectoryName(currentPath);
            }
            string selectedPath = EditorUtility.OpenFilePanel("选择 PSD 文件", directory, "psd");
            if (string.IsNullOrEmpty(selectedPath))
                return;
            string relativePath = Path.GetRelativePath(root, selectedPath).Replace('\\', '/');
            string error = ValidateSource(relativePath);
            if (error != null)
            {
                EditorUtility.DisplayDialog("选择 PSD 文件", error, "确定");
                return;
            }
            sourceAddress = relativePath;
            EditorPrefs.SetString(SourceKey, sourceAddress);
            GUI.changed = true;
        }

        private string ValidateSource(string source)
        {
            if (string.IsNullOrWhiteSpace(source))
                return "请输入 PSD 文件或文件夹的项目内相对路径。";
            string value = source.Trim();
            if (Path.IsPathRooted(value) || value.IndexOfAny(new[] { ':', '"', '<', '>', '|', '?', '*' }) >= 0)
                return "PSD 源路径必须是项目内相对路径。";
            string root = Path.GetDirectoryName(Application.dataPath);
            string fullPath = Path.GetFullPath(Path.Combine(root, value));
            if (!fullPath.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                return "PSD 源路径不能超出项目目录。";
            if (File.Exists(fullPath))
                return fullPath.EndsWith(".psd", StringComparison.OrdinalIgnoreCase) ? null : "源文件必须是 PSD。";
            if (Directory.Exists(fullPath))
                return GetPsdFiles(fullPath).Length > 0 ? null : "文件夹中没有 PSD 文件。";
            return "找不到 PSD 文件或文件夹。";
        }

        private static string[] GetPsdFiles(string folder)
        {
            return Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Where(path => path.EndsWith(".psd", StringComparison.OrdinalIgnoreCase))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        private static string ValidateDirectory(string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
                return "请输入以 Assets/ 开头的 UI 导出路径。";
            string path = relativePath.Trim().Replace('\\', '/');
            if (!path.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) ||
                path.IndexOfAny(new[] { ':', '"', '<', '>', '|', '?', '*' }) >= 0 ||
                path.Split('/').Any(part => part.Length == 0 || part == "." || part == ".."))
                return "UI 导出路径必须是 Assets/ 下的项目内相对路径。";
            return null;
        }

        private static string NormalizeCachedPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;
            string normalized = path.Trim().Replace('\\', '/');
            return normalized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                ? normalized : "Assets/" + normalized;
        }

        private void StartExport(string mode)
        {
            string root = Path.GetDirectoryName(Application.dataPath);
            string scriptName = "export_psd.py";
            string scriptPath = Path.Combine(root, "Tools", "PsdUgui", scriptName);
            if (NeedsPython(mode) && !File.Exists(scriptPath))
            {
                EditorUtility.DisplayDialog("资源导出", $"找不到 Tools/PsdUgui/{scriptName}。", "确定");
                return;
            }
            string source = sourceAddress.Trim();
            string fullPath = Path.GetFullPath(Path.Combine(root, source));
            sources = new Queue<string>(Directory.Exists(fullPath)
                ? GetPsdFiles(fullPath).Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                : new[] { source.Replace('\\', '/') });
            exportPath = uiPath.Trim().Replace('\\', '/');
            completed = 0;
            batchSource = source;
            exportedPages.Clear();
            exportedPrefabPaths.Clear();
            total = sources.Count;
            exporting = true;
            exportMode = mode;
            exportGreenOnly = greenOnly;
            exportLineSpacing = lineSpacing;
            StartNext();
        }

        private void StartNext()
        {
            currentSource = sources.Dequeue();
            string root = Path.GetDirectoryName(Application.dataPath);
            string scriptPath = Path.Combine(root, "Tools", "PsdUgui", "export_psd.py");
            string python = Application.platform == RuntimePlatform.WindowsEditor ? "python" : "python3";
            status = $"正在导出 {completed + 1}/{total}：{currentSource}";
            Repaint();
            exportTask = !NeedsPython(exportMode) || completed > 0
                ? Task.FromResult(new ExportResult(0, string.Empty, string.Empty))
                : Task.Run(() => RunExport(python, scriptPath, root, batchSource, exportPath, exportMode, exportGreenOnly));
            EditorApplication.update += PollExport;
        }

        private void PollExport()
        {
            if (exportTask == null || !exportTask.IsCompleted)
                return;
            EditorApplication.update -= PollExport;
            ExportResult result = exportTask.IsFaulted
                ? new ExportResult(-1, string.Empty, exportTask.Exception?.GetBaseException().Message)
                : exportTask.Result;
            exportTask = null;
            if (result.ExitCode != 0)
            {
                string detail = string.IsNullOrWhiteSpace(result.Error)
                    ? $"退出码 {result.ExitCode}，请查看 Console。" : result.Error.Trim();
                status = $"导出失败：{currentSource}。{detail}";
                UnityEngine.Debug.LogError($"资源导出失败：{result.Error}\n{result.Output}");
                exporting = false;
                Repaint();
                return;
            }

            try
            {
                AssetDatabase.Refresh();
                if (exportMode == "images" || exportMode == "all")
                {
                    string texturePath = Path.Combine(exportPath, "Textures");
                    if (Directory.Exists(texturePath))
                    {
                        foreach (string imagePath in Directory.GetFiles(texturePath, "*.png", SearchOption.AllDirectories))
                        {
                            if (Path.GetFileNameWithoutExtension(imagePath).EndsWith("_9s", StringComparison.OrdinalIgnoreCase))
                                AssetDatabase.ImportAsset(imagePath.Replace('\\', '/'), ImportAssetOptions.ForceUpdate);
                        }
                    }
                }
                if (exportMode == "all" || exportMode == "prefab" || exportMode == "components" || exportMode == "scripts")
                {
                    string relativePath = exportPath.Substring("Assets/".Length);
                    foreach (string pageName in ExportPrefabUtil.GetPageNames(relativePath, Path.GetFileNameWithoutExtension(currentSource)))
                    {
                        if (!exportedPages.Add(pageName))
                        {
                            UnityEngine.Debug.LogWarning($"跳过同名画板预制体：{pageName}");
                            continue;
                        }
                        string assetPath = ExportPrefabUtil.GetPrefabAssetPath(relativePath, pageName);
                        if (exportMode == "all" || exportMode == "prefab")
                        {
                            assetPath = ExportPrefabUtil.Export(relativePath, pageName, out string[] missingImages, exportLineSpacing);
                            UnityEngine.Debug.Log($"预制体导出完成：{pageName}；缺失图片：{(missingImages.Length == 0 ? "无" : string.Join("、", missingImages))}");
                        }
                        if (AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) == null)
                            throw new FileNotFoundException($"找不到预制体：{assetPath}，请先执行导出预制体。");
                        if (exportMode == "all" || exportMode == "components")
                        {
                            PsdPrefabComponentBinder.Bind(assetPath);
                            UnityEngine.Debug.Log($"组件绑定完成：{assetPath}");
                        }
                        exportedPrefabPaths.Add(assetPath);
                        EditorGUIUtility.PingObject(AssetDatabase.LoadAssetAtPath<GameObject>(assetPath));
                    }
                    UnityEngine.Debug.Log(result.Output ?? "导出完成");
                }
                else
                    UnityEngine.Debug.Log(result.Output);

                // 所有画板处理结束后统一生成，编译重载不会打断 PSD 批处理队列。
                if (sources.Count == 0 && (exportMode == "all" || exportMode == "scripts"))
                    UIBindCodeGenerator.GenerateForPrefabs(exportedPrefabPaths
                        .Select(path => AssetDatabase.LoadAssetAtPath<GameObject>(path)));
            }
            catch (Exception error)
            {
                status = $"处理失败：{currentSource}：{error.Message}";
                UnityEngine.Debug.LogError($"PSD 处理失败：{error}");
                exporting = false;
                Repaint();
                return;
            }

            completed++;
            if (sources.Count > 0)
                StartNext();
            else
            {
                status = exportMode == "all" || exportMode == "scripts"
                    ? $"脚本生成已提交：{exportedPrefabPaths.Count} 个预制体，编译完成后自动绑定组件。详情见 Console。"
                    : $"处理完成：{completed} 个设计稿 → {exportPath}";
                exporting = false;
                Repaint();
            }
        }

        private static bool NeedsPython(string mode)
        {
            return mode == "structure" || mode == "images" || mode == "all";
        }

        private static ExportResult RunExport(string python, string scriptPath, string root, string source, string path,
            string mode, bool onlyGreen)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = python,
                Arguments = $"{Quote(scriptPath)} --source {Quote(source)} --project-root {Quote(root)} --assets-path {Quote(path)}" +
                            $" --mode {mode}" + (onlyGreen ? " --green-only" : string.Empty),
                WorkingDirectory = root,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };
            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                Task<string> stdout = process.StandardOutput.ReadToEndAsync();
                Task<string> stderr = process.StandardError.ReadToEndAsync();
                process.WaitForExit();
                return new ExportResult(process.ExitCode, stdout.GetAwaiter().GetResult(), stderr.GetAwaiter().GetResult());
            }
        }

        private static string Quote(string value)
        {
            return "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
        }

        private readonly struct ExportResult
        {
            public readonly int ExitCode;
            public readonly string Output;
            public readonly string Error;
            public ExportResult(int exitCode, string output, string error)
            {
                ExitCode = exitCode;
                Output = output;
                Error = error;
            }
        }
    }
}
