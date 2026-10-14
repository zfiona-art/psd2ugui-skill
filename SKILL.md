---
name: psd-ugui-workflow
description: 将 PSD 设计稿转换为 Unity UGUI 布局、切图、预制体、交互组件及 C# 引用绑定。自带独立导出器源码与接入脚本，可用于尚未安装导出工具的新 Unity 项目，也支持现有工具维护与故障排查。不用于普通图片编辑或 UI Toolkit。
---

# PSD UGUI 工作流

## 确认项目与任务

从当前工作目录确认 Unity 项目根目录，不固定用户机器的绝对路径。先检查工作区改动，保留无关修改。读取项目的 AGENTS.md，以及实际存在的 `Doc/工具使用说明.md` 和 `Doc/美术规范.md`；目标项目约定及用户明确要求优先；本技能自带独立源码，不需要访问原 VectoUI 项目。

按任务需要读取参考：

- 新项目接入或目标框架适配：先读 [新项目接入](references/new-project.md)。
- 画板、命名、图层结构和美术交付检查：读 [美术规范快照](references/art-spec.md)。
- 参数、各步输入输出、目录及日常更新：读 [工具流程快照](references/workflow.md)。

自带工具在 [assets/toolkit](assets/toolkit)，安装脚本为 [scripts/install_toolkit.py](scripts/install_toolkit.py)。要求在新项目导出时，将工具接入视为完成任务所需步骤；先预检冲突，再安装和编译，不因目标缺少旧导出器而停止。目标已有同类工具时先比较，避免重复注册导出流程或覆盖手工修改。仅要求检查或解释时不安装。

用户仅询问原理或要求检查时执行只读调查；要求导出、修复或修改时完成相应工作。根据源 PSD、导出路径和任务推断可得参数，只询问影响结果且无法确定的信息。

## 选择最小必要流程

入口为 Unity 菜单 `Tools/PSD UGUI/Export`。源文件或文件夹路径相对项目根目录，输出路径必须位于 `Assets/` 下。

| 任务 | 执行步骤 |
| --- | --- |
| 首次生成或整体更新 | 结构 → 图片 → 预制体 → 绑定组件 → 生成脚本；也可一键导出 |
| 同名同尺寸图片像素更新 | 导出图片 |
| 位置、尺寸、文字或层级改变 | 结构 → 预制体 → 组件 → 脚本；图片有变化时同时导出图片 |
| 仅刷新已修改预制体的字段 | 生成脚本 |

只有结构和图片阶段由 Python 完成。需要时先检查当前脚本 `--help`，再从项目根目录调用，例如：

```sh
python3 Tools/PsdUgui/export_psd.py --source Doc/example.psd --project-root . --assets-path Assets/Art/UI --mode all --green-only
```

示例路径和过滤开关必须按实际任务替换；Windows 编辑器调用 `python`。Python 的 `--mode all` 只生成布局和图片，**不代表 Unity 的一键导出全部完成**。检查解释器及 psd-tools/Pillow 是否可用；缺少依赖时准确报告，不把未执行的步骤当成成功。

Unity 阶段优先使用可用的 Unity 连接工具或 UI 操作，先确认连接的是当前项目。不要同时启动第二个 Unity 实例处理已打开的同一项目。没有可用编辑器连接时，完成可执行的检查，明确剩余步骤；不要为绕过连接限制修改生产入口或伪造预制体 YAML。

## 关键契约

- 一画板一份布局和预制体，无画板时使用 PSD 文件名。画板清单在 `Config/psd/Manifests`；后续单独步骤依赖已有清单和布局。
- 绿色标签仅过滤图片导出，不删除结构节点或旧文件。隐藏节点仍会生成并记录非激活状态。
- `_bg` 路由到 `Textures/Big`，其他图片到 `Textures/Main`；不是自动按像素尺寸分类。`com_` 是资源命名约定，不会自动合成组件行为。
- `_9s` PNG 内嵌 `UnityNineSlice` 边框元数据；保留 PNG 导入读取和 Image.Type.Sliced 配套，不改为旁置 `.9slice` 文件。
- 同批次图片和画板名称不区分大小写去重，首个生效；磁盘已有同名输出会重写。重新生成预制体会影响手工修改，执行前检查目标文件的改动并保留需要的工作。
- 缺图汇总后允许生成，缺字体会失败。不要默默用默认字体掩盖问题。
- 名为 `top` 的组绑定 TopAreaAdapter，边缘组控制锚点。弹窗 `content` 应为根的直接子节点，独立 PsdUguiViewBase 可配置 Show/Hide 缩放动画。
- 当前组件绑定器要求 Toggle 有唯一 `_on`、`_off`；InputField 两者都须为 Text；Slider 必须有 `_back`、`_front`、`_move` 三张 Image。附带美术规范中滑块可选的表述不能覆盖此实际要求。
- ScrollRect 使用 `viewport/content`，绑定时添加裁剪、对应方向布局和 ContentSizeFitter；列表位置可能由布局重新计算。
- 组件绑定器名称匹配不区分大小写，但当前代码字段扫描区分大小写。使用约定前缀和合法、同一绑定范围内唯一的 C# 标识符，避免重复字段或绑定到首个同名对象。
- 脚本输出固定在 `Assets/Scripts/View` 和 `Item`，不随资源路径改变。更新自动生成字段区域，业务逻辑保留在区域外。
- 新建及修改 `.cs` 均使用 UTF-8 BOM，保留已有换行风格；生成器显式使用 `new UTF8Encoding(true)`。

## 维护与故障排查

先定位故障所在阶段，检查当前实现，不仅修改生成产物：

- 窗口、路径、批处理、步骤调度：`Assets/PsdUgui/Editor/ExportWindow.cs`。
- PSD、文字属性、PNG、去重和清单：`Tools/PsdUgui/export_psd.py`。
- 字体、Sprite、布局、文字尺寸和锚点：`ExportPrefabUtil.cs`。
- 控件及内部节点引用：`PsdPrefabComponentBinder.cs`。
- PNG 九宫格读取和平台贴图设置：`TextureImportSettings.cs`，核对路径过滤范围。
- 字段扫描、Item 生成、编译后挂载与绑定：`UIBindCodeGenerator*.cs`。
- 运行时行为：`Assets/PsdUgui/Runtime` 内的 TopAreaAdapter、ToggleStateAdapter 和 PsdUguiViewBase。

基础工具已隔离原项目框架、Spine/DOTween 依赖。接入目标现有框架时，按新项目参考同时适配基类、生成器类型判断、运行时入口和程序集引用，保留目标接口。不要把源码复制或静态编译通过当成 Editor 全流程完成。

## 验证与交付

文档编辑核对链接和实现即可。代码修改按影响范围执行语法、差异、BOM/换行及可用编译检查；避免仅验证生成文本与实现相同的无意义测试。

导出后检查文件、画板清单、重复跳过及缺图日志。生成脚本后等待 Unity 编译和自动绑定完成，检查主界面及 Item 脚本挂载、字段引用，再检查按钮、Toggle、InputField、Slider、滚动区、九宫格和文字。屏幕适配及动画需要运行时或设备验证。

最终说明产物路径、实际执行的阶段、检查结果及未完成的编辑器或设备验证。窗口显示“脚本生成已提交”、Python 返回成功或静态检查通过，都不能单独证明整个工作流完成。
