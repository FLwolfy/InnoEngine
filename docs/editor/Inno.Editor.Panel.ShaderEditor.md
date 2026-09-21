# Inno.Editor.Panel.ShaderEditor

[Editor 索引](README.md) · [Wiki 首页](../README.md) · [Graph 控制层](Inno.Editor.Graph.md) · [Shader 模型](../render/Inno.Rendering.Shaders.md)

## 职责与边界

内置 `.ishader` 编辑界面，替代已经移除的 Material Graph Panel。`.imaterial` 仍只保存 Shader 引用及参数，不承载图。画布跟随 File Browser 当前 Shader 选择；双击 Shader 打开并聚焦。没有固定侧栏或路径输入框；画布 Header 第一行是当前 Shader 下拉选择，第二行提供 Save / Revert / Format / Check，星号表示尚未应用的草稿。没有选中 Shader 时 Header 仍然存在，显示 `Select Shader` 下拉选择，四个文档操作按钮禁用；其下使用带 Panel padding 的居中空状态。

当前实现与完整验收必须区分：右键菜单、节点值编辑、捕获式平移、鼠标锚点缩放、框选、节点移动、连接、复制粘贴、显式保存已经接线；完整 UI 实操、所有高级节点/资源操作和最终渲染一致性尚待验收。详见[实施状态](../issues/2026-09-11-unified-shader-implementation.md)。

## 初始化与生命周期

宿主提供 AssetPipeline、SerializationRegistry、TypeCatalog、GraphEditorModule、EditorInteractions、EditorShaderCompilation、AssetImportSettingsEdits 和 IEditorPreviewService。ShaderEditorDocuments Module 使用 LifetimeScope 拥有当前 generation 的节点、前端、drawer registry 和 headless 文档注册；停止时只保留未保存恢复数据并按依赖顺序退休，不写入源资产。Shader Editor 是唯一图画布，Document Service 只管理单实例、Save/Revert、恢复和 reload 生命周期，不再提供可见 Documents Panel。

文件条目 identity 与资产 identity 是不同身份：从文件条目的 `assetPath` 查询 AssetInfo，再以 `AssetInfo.persistentId` 打开图文档、查询编译、引用源码。不能拿文件条目的 ID 调用资产加载或当成 Shader/source reference。

图修改统一进入 GraphDocumentController / EditorInteractions.history。节点拖动只在释放时提交；数值与文本使用独立 gesture ID。平移、缩放只保存为按资产 persistent ID 索引的视图状态，不恢复 File Browser 选择。

## 公开扩展 API

下列扩展已移入 [Inno.Editor.Shaders](Inno.Editor.Shaders.md)，本 Panel 仅作为使用方，不再拥有公共节点创作协议。

| 类型 / 成员 | 契约 |
| --- | --- |
| `ShaderNodeDrawerAttribute(definitionId, displayName, createPath, createOrder, separatorBefore)` | 注册稳定节点 ID 与 Editor-only 呈现；插件自主贡献创建目录、顺序和同级分隔，重复 ID 拒绝候选 |
| `ShaderNodeDrawer.Draw(ShaderNodeDrawContext)` | 在统一 Inspector 绘制选中节点的控件；不编译 Shader，不保存当前帧 context |
| `ShaderNodeDrawContext.previews` | 帧内使用共享 generation-scoped 预览；不得缓存过期 handle |
| `ShaderNodeDrawContext.nodeId` | 用于稳定控件身份的节点 ID |
| `Read<T>(key, defaultValue)` | 通过当前 owner 的序列化上下文读取独立值，损坏值不替换为默认 |
| `Write<T>(key, value, continuous)` | 写入中立草稿属性并进入统一 History；不直接保存或发布资产 |

这些类型在 EditorScripts 中使用逻辑命名空间 `InnoEditor.Shaders`。普通 runtime scripts 不引用该项目。编译扩展继续属于 `InnoEditor.Rendering.Shaders`，与 UI drawer 独立。

```csharp
using InnoEditor.Shaders;

[ShaderNodeDrawer("example.surface")]
public sealed class SurfaceDrawer : ShaderNodeDrawer
{
    public override void Draw(ShaderNodeDrawContext context)
    {
        float current = context.Read("gain", 1f);
        // Draw a shared Editor numeric widget here and call Write only after a user edit.
        // context.Write("gain", editedValue, continuous: true);
    }
}
```

## 保存与错误

- 编辑、拖动、连接、Undo/Redo 只改变草稿；Shader Editor 的 Save 按钮或画布聚焦时 Command/Ctrl+S 才写入 `.ishader`。保存不以图编译成功为条件。
- 写盘前先保留 Library/Editor/ShaderRecovery 中的中立恢复数据；比较上次读取的源指纹，已发生的外部修改拒绝覆盖。
- 文件切换、关闭 Shader Editor 面板和停止不应用草稿。恢复文件只位于 Library，不参与资产导入或 GPU 发布。失败保留文档、历史与恢复文件，并在画布显示错误。
- 源码外部更新：未编辑文档接受新源；dirty 文档显示冲突，不覆盖磁盘。暂时缺失源保留图和 Undo barrier。
- Save 与编译是不同状态；编译状态明确标记为 Saved asset。保存完成后在下一次 Editor Update 请求导入，不依赖 watcher 延迟；无效已保存图继续显示失败和 last-good，不伪装成成功。
- 安装资产只读，可查看；右键“Copy Shader to Project”创建独立项目资产并选中，创建可撤销。
- 画布空白区域的 Assets 菜单始终提供“Show Shader in File Browser”；它定位当前 `.ishader`，不要求先选中节点。源码节点另行提供“Show Source in File Browser”。不再提供“Create Material From Shader”；Material 从 File Browser 的统一 Create 菜单建立后再显式选择 Shader。
- Close 由共享文档服务处理 Save/Discard/Cancel；provider 不在 Discard 后偷偷 Save。
- Revert 恢复已保存内容，可通过 Undo 找回草稿；Undo 后仍需 Save 才会应用。
- 源码节点的 `Apply Import Settings` 是对所选 `.ishadersource.imeta` 的独立显式操作，可能影响引用该源码的其他 Shader；它不代替当前 Shader 图的 Save。编辑源码文件本身仍使用 IDE 保存。
- 删除支持 Delete 与 Backspace。每个低级 Shader 最多拥有一个 Vertex、Fragment 和 Compute Output；创建菜单按 Output 单独创建，已存在的阶段禁用，复制/粘贴与 Duplicate 不能绕过该不变量。删除 Output 同时删除引用它的 Pass、配对后失去引用的阶段内容及声明。删除最后一个参数输入清理其声明，共享输入保留默认值和剩余阶段可见性。事务显式 Commit，一次操作对应一次 Undo。

## 当前限制

节点参数在 Inspector 编辑，不在画布重复一套字段；输入值本身只由 Graph 连接决定。Optional 输入在未连接时由编译器生成精确类型的零值，Inspector 只显示 `Optional · Zero when unconnected`，不保存或提供外部 override。Required 输入必须在 Graph 中连接 Constant 或其他类型兼容的 output；旧文档里残留的 `input-default.*` 不能再让 required 输入通过编译。资源类型若不能表示零值则不能声明为 optional。连接后只显示上游来源。输入 label 只保留端口名；类型和来源统一留在右侧 value column，以 `Float4 · From tint-multiply.value` 这类普通弱化文字表达。所有 Inspector fieldset 都可直接点击标题文字折叠，折叠后保留中断横线和左右居中的短竖帽，不显示额外加减号或整行 hover 背景。Preview、连接来源、错误提示和其他 Hint 都按当前 fieldset 宽度自动换行。
Inspector 的 Draft Preview 只在用户显式执行 Check 且当前草稿编译成功后显示；任何后续草稿修改都会使其失效并要求重新 Check。Check 失败时 Inspector 只提示失败，不显示详细错误，也不呈现编译器缓存中的 last-good 候选；完整成功/失败诊断统一进入 Console。未经 Save 的预览不进入正式资源发布；它当前是编译预览，不是完整材质画面预览。

Pass/Variant、Technique/Role、自定义混合和能力要求在 Output Inspector 中编辑，不再通过“Create Pass”一次生成一组可重复 Output。存储读写、原子加法和 discard 节点通过显式 after/then 连线约束副作用顺序。右键沿用共享菜单与搜索，并分为 Create、View、Edit、Connections、Organize 与 Assets；分隔线只标示同级语义边界或插件贡献的顶级函数目录，不向父级和每个子项传播。Group 可被选中，拖动组标题会整体移动成员；组名位于独立 Header，Header 颜色取成员节点 Header 颜色的混合。Insert Reroute 在当前连接线上插入一个强类型、零运算的布线点，只整理长连线，不改变生成的 Shader 语义。Format 使用分层依赖布局和多轮端口感知的交叉最小化：输入在左、Output 在右，并按目标端口次序排列同层来源；它只改画布位置且可 Undo。源码导入设置通过 .imeta 与共享 History 编辑；`catalogPath`/`catalogOrder` 让插件把函数库放入自己的可读菜单分组。端口快照只保存中立类型/身份，缺失端口以红色保留，不按序号重连。

菜单项的 enabled 状态统一来自 `EditorAction.Query`，不是表现层根据 label 猜测。显式 Vertex/Fragment/Compute Output 的查询检查资产只读状态、图是否已经交给领域 Target，以及对应 Stage Output 是否已经存在；每个显式 Stage 只允许一个，因此已存在的项保持可见但禁用。领域 Output 和其他插件节点通过相同 Action/Menu 模型及节点扩展注册进入菜单，通用 Editor 不判断 Rendering2D 类型。

`.ishadersource` 是显式函数库：Import Settings 中列出的每个函数名都是独立公开 API，未列出的函数是私有 helper；一个文件可以导出多个函数，右键 Create / Functions 按“插件目录 / 文件 / 函数”创建节点，不存在默认 Source 或隐式 `main`。节点的 Show in File Browser 只在引擎 File Browser 中定位资产，不启动操作系统或外部 IDE。顶部 Check 对当前草稿进行隔离编译，不保存、不发布；它使用与脚本/插件重载相同的默认固定宽度、居中位置、遮罩、淡入淡出和阻塞生命周期，不显示进度条。编译完成后无论成功或失败都自动关闭，结构化结果及源码位置统一发布到 Console，不在 Modal 内建立第二套诊断浏览器。

可复用节点本身也是 `.ishader`。节点名称、创建目录、排序、Kind、Effect 与 Role 是 Graph 级设置，不再寄存在某个输入节点上。Reusable Node 模板提供 Function Inputs 与 Function Outputs；两个方向可独立删除或建立，只要至少一个方向拥有端口。这样 output-only 生成器、input-only SideEffect 与普通双向函数使用同一资产格式。保存后，Shader Editor 从资产接口快照自动生成 **Create / Graph Nodes** 菜单项和调用节点，不要求插件再贡献同名 Drawer/Compiler。普通 Function 子图在导入时内联；Domain Output 子图按 Role 留给插件 Target。节点引用使用资产 persistent ID，路径只是可修复诊断信息；引用环、旧端口和接口不完整在 Check/Console 中明确报告。

跨 Stage 值可以直接连线：Vertex 输出接到 Fragment 节点时，编译器自动推导并生成 transient varying，同一来源的多路使用只生成一份桥接接口；这些节点不会污染作者画布。其他跨 Stage 方向或资源句柄传递明确报错。**Organize / Collapse to Subgraph** 会把同一 Stage 的当前选择抽成新的 `.ishader`，自动推导多输入/多输出，在原图中以一个 Graph Node 替换且进入统一 History；组名会作为建议节点名，未分组时使用可重命名的 `Shader Node`。

Shader 与节点 Inspector 不重复画布 Header 的 Save / Revert / Format / Check。Shader Inspector 始终显示草稿专用 Preview（无开关），所有 Target、参数、节点设置和输入默认值复用通用 Inspector 的“左侧 label、右侧控件”Property Row；草稿预览仍不会修改资产或 Scene/Game。
节点本体不再绘制“Select to edit in Inspector”占位尾部。Header 使用不可配置的语义色系：输入/常量为暖橙、Output 为紫色、采样为青绿、源码与数学/构造节点使用相邻但可区分的蓝青色、存储为绿色、副作用终止为红色；Graph Node 再按稳定 ID 与名称从受约束色域生成固定颜色，所以大量插件节点仍可区分且不会在重载后随机变色。同类节点保持同一视觉语言，不为每个实例随机着色。Required 输入使用实心端口和正常文字，Optional 输入使用空心端口及弱化文字，悬停会明确显示 required/optional。所有 `Add …` 集合按钮在 Inspector 中居中。选中节点时 Inspector 顶部仍显示所属 `.ishader` 文件名，第二行显示 `Node: <节点名>` 或多选数量。端口描述阶段发现的节点错误保留画布红点和 tooltip，同时作为带稳定节点 semantic ID 的结构化诊断发布到 Console，修复节点后对应诊断自动清除。

Stage Input 的 `Source = Builtin` 表示该值由 GPU 阶段或引擎/Adapter 的标准阶段环境提供，而不是来自顶点缓冲、Material uniform、纹理或上游 varying。图保存后端中立 semantic，例如 Vertex 的 `vertex-id`/`instance-id`/`view-projection`、Fragment 的 `fragment-coordinate`/`front-facing`/`view-rectangle`，以及 Compute 的 `global-invocation-id`；Target 与 Adapter 必须共同支持该 semantic 和精确类型，否则 Check 产生错误。它不是任意源码表达式，也不是让用户填原生变量名的旁路。
完整 UI 实操和热重载回归仍在最终验收清单中，不能把接线完成等同为验收通过。
