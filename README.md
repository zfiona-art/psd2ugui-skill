# psd-ugui-workflow 使用说明

将 Photoshop PSD 设计稿转换为 Unity UGUI 布局、PNG 切图、UI 预制体、交互组件和 C# 字段绑定。Skill 自带独立导出器源码及接入脚本，新项目无需预先安装 VectoUI 导出器。

## 在 Codex 中使用

在目标 Unity 项目的 Codex 聊天中输入以下请求，将 PSD 路径和输出目录替换为实际值：

```text
使用 $psd-ugui-workflow 在当前项目接入工具，
将 Doc/design.psd 导出到 Assets/Art/UI，执行完整流程。
```

源地址相对 Unity 项目根目录，可以是单个 PSD，也可以是包含 PSD 的文件夹；输出目录必须位于 `Assets/` 下。需要只导出绿色标签图片时，在请求中明确说明。

### 常用请求

**先检查设计稿，不导出：**

```text
使用 $psd-ugui-workflow 检查 Doc/design.psd 是否符合美术规范，先不要导出。
```

**批量导出文件夹内的 PSD：**

```text
使用 $psd-ugui-workflow 将 Doc/PSD 下的所有 PSD 导出到 Assets/Art/UI，
只导出带绿色标签的图片，并执行完整流程。
```

**仅更新切图：**

```text
使用 $psd-ugui-workflow 仅更新 Doc/design.psd 的切图，输出到 Assets/Art/UI。
```

仅更新切图适合图片名称、尺寸和布局不变的情况；结构或尺寸改变时应重新生成布局及预制体。

**刷新已有预制体的脚本字段：**

```text
使用 $psd-ugui-workflow 为 Assets/Art/UI/Prefabs/UI/UIMain.prefab
重新生成脚本字段并绑定引用。
```

**排查组件绑定错误：**

```text
使用 $psd-ugui-workflow 排查当前项目的组件绑定错误，检查必要子节点和组件引用。
```

## 首次接入会做什么

1. 检查目标项目的 Unity 版本、UGUI 依赖、现有 UI 框架和文件冲突。
2. 将编辑器工具安装到 `Assets/PsdUgui/Editor`，运行时组件安装到 `Assets/PsdUgui/Runtime`。
3. 将 Python 导出器及依赖清单安装到 `Tools/PsdUgui`，检查 Python、psd-tools 和 Pillow 环境。
4. 等待 Unity 导入和编译，然后根据请求执行导出。

接入脚本发现目标文件已有不同内容时，会停止写入，避免覆盖修改。基础工具不依赖原项目框架、Spine 或 DOTween，使用 UGUI Text/InputField；TMP 或现有 UI 基类需要额外适配。

接入脚本也可手动调用，详见[新项目接入](references/new-project.md)。

## 完整导出流程

| 步骤 | 处理内容 |
| --- | --- |
| 导出结构 | 读取画板、图层层级、位置、尺寸、文字及可见性，生成布局 JSON 和画板清单 |
| 导出图片 | 生成 PNG，按后缀分类背景图，并为九宫格图片嵌入边框信息 |
| 导出预制体 | 根据布局生成节点、Image、Text、锚点及隐藏状态 |
| 绑定组件 | 配置 Button、Toggle、InputField、Slider、ScrollRect 和顶部安全区适配 |
| 生成脚本 | 生成界面及 Item 字段，等待 Unity 编译后挂载脚本并绑定引用 |

Python 脚本的 `--mode all` 仅执行结构和图片导出；完整流程还需要 Unity Editor。窗口显示“脚本生成已提交”后，应等待编译和引用绑定完成，再检查 Console 与预制体。

## 在 Unity 中手动操作

接入并编译完成后，打开 **Tools → PSD UGUI → Export**：

1. 设置源地址，例如 `Doc/design.psd`。
2. 设置 UI 导出路径，例如 `Assets/Art/UI`。
3. 按需勾选绿色标签过滤，设置多行文字行距。
4. 点击“一键导出”，或依次执行五个步骤。
5. 检查 Console、预制体、脚本引用及 Canvas 下的视觉和交互表现。

字体需提前导入项目。业务事件、界面管理和数据加载逻辑由项目实现；生成预制体会重建内容，已有手工调整需先保留。完整操作说明见[工具流程](references/workflow.md)。

## 进一步阅读

- [技能执行规则](SKILL.md)
- [新项目接入与框架适配](references/new-project.md)
- [美术规范](references/art-spec.md)
- [工具流程及注意事项](references/workflow.md)
