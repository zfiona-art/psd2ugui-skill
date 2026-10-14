# 新项目接入

## 工具包内容

`assets/toolkit` 是可复制的独立源码，不依赖原 VectoUI 工程，也不读取其机器路径。

| 安装位置 | 内容 |
| --- | --- |
| `Assets/PsdUgui/Editor` | 导出窗口、预制体构建、组件绑定、字段生成及 PNG 九宫格导入器 |
| `Assets/PsdUgui/Runtime` | 独立 PsdUguiViewBase、安全区适配、Toggle 状态适配和组件扩展 |
| `Tools/PsdUgui` | PSD Python 导出脚本和 requirements.txt |

编辑器与运行时代码使用 `PsdUgui.Editor` / `PsdUgui` 命名空间和独立 asmdef。基础依赖为 Unity UGUI、Python 3、psd-tools 和 Pillow。没有 FrameWork、UIManager、消息总线、Spine 或 DOTween 硬依赖。基于 Unity 2022.3 API 编写，其他版本先编译验证。

## 接入步骤

1. 读取目标项目 AGENTS.md、Unity 版本、Packages/manifest.json、已有 UI 基类和工作区改动。确认是 UGUI 项目；当前模板使用 Text/InputField，不自动替换为 TMP 或 UI Toolkit。
2. 使用安装脚本预检，然后安装。以下 `<skill-dir>`、`<project-root>` 应替换为当前实际路径，路径参数始终正确引用：

```sh
python3 "<skill-dir>/scripts/install_toolkit.py" --project-root "<project-root>" --dry-run
python3 "<skill-dir>/scripts/install_toolkit.py" --project-root "<project-root>"
```

脚本先检查全部目标文件，内容不同则整体停止写入，重复安装相同版本不改文件。不携带旧 `.meta`；由目标 Unity 生成 GUID。升级时比较差异并合并已适配源码，不删除整目录重装。安装脚本不改目标的 Packages/manifest.json。

3. 确认目标版本已安装 `com.unity.ugui`。使用 Unity Package Manager 处理缺失依赖，保留其他包；不盲目固定跨版本包号。
4. 在目标项目适合的 Python 环境安装 `Tools/PsdUgui/requirements.txt`。Unity 窗口默认调用 Windows 的 `python` / 其他系统的 `python3`；若使用虚拟环境，需要确保 Unity 进程 PATH 能找到该解释器，或将窗口的解释器配置适配为实际路径。不要在不相关的全局环境安装包。
5. Unity 完成导入和编译后，打开 `Tools → PSD UGUI → Export`。源地址相对项目根目录，输出路径位于 Assets 下。
6. 准备字体和 PSD，执行一键导出；等待脚本编译及引用自动绑定，然后进行 Editor 和运行时检查。

## 与目标 UI 框架适配

新项目默认生成继承 `PsdUgui.PsdUguiViewBase` 的界面脚本，只提供 Show/Hide 和可选缩放动画，不附带 UI 路由、资源加载、消息总线或业务管理器。Inspector 中开启 animatePopup 后，Show/Hide 使用直接子节点 content；只 SetActive 不会自动执行 Show 动画。

已有 UI 框架时优先保留目标基类和接口。需要集成时，一并适配生成器模板、类型检查、组件获取、Item 判定及自动绑定，不仅替换模板中的继承名称。不要复制原项目 ViewBase 及其完整业务框架。若现有同名界面脚本不继承当前配置基类，应先明确并实施适配，不能覆盖原脚本。

基础工具不包含 Spine 字段支持；项目确有 Spine 且用户需要时再增加匹配及生成 using，检查 asmdef 引用。TMP 同理，应同步调整文本解析后的组件创建、字体匹配、输入框和字段绑定。

九宫格导入器只识别 `_9s` PNG 的 UnityNineSlice 元数据，不强制目标项目 Android 压缩策略、最大纹理尺寸或 grid 命名规则。现有 AssetPostprocessor 仍可能影响结果，需在目标项目检查最终 Sprite.border。

## 验证范围

接入脚本预检和独立 C# 编译只能证明安装与 API 引用层面的结果。完整验证还需目标 Unity 的程序集编译、真实 PSD 导出、prefab 保存、脚本重载后引用绑定，以及 Canvas 下的交互与目标设备安全区表现。
