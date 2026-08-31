# Inno.Editor.Panel.FileBrowser

[Editor 索引](README.md) · [Assets](../assets/README.md) · [Hierarchy](Inno.Editor.Panel.Hierarchy.md)

该项目完整拥有 File Browser feature：Tree/List/Grid 表现、导航与过滤、Asset selection、AssetEditor 扩展、文件操作 Action、菜单、Asset drag source，以及 `AssetFileEntry` 的 `AssetSelectionInspectionDrawer`。它只引用共享的 `Inno.Editor.Inspection`，不引用 Hierarchy 或 Inspector Panel。

Scene、Prefab、Folder 和普通文件 icon declaration 可以直接保存完整 Settings path；`AssetIconRegistry` 用 `EditorSettings.Get(path).GetAsString("value")` 读取 glyph。脚本声明仍可填写 literal glyph。Game Layers 已是项目根 `EditorSettings.json` 数据，不再作为 FileBrowser entry、extension icon 或 Asset inspection target。

## 公共扩展 API

| API | 作用 |
| --- | --- |
| `AssetBrowserState` | 按 persistent identity 保存当前目录与选择。 |
| `AssetEditor` / `AssetEditorAttribute` | 为特定 Asset 类型声明 Open/Rename/Delete/Drag 行为。 |
| `AssetEditorContext` | 当前 `EditorContext`、interactions、路径、Asset 信息和实例。 |
| `AssetIconAttribute` / `AssetIconKind` | 按 imported Asset 类型或 source extension 配置 Tree/List/Grid 共用图标。 |
| `AssetEditorModule.GetIcon` | 为其他 Editor presentation 解析完全相同的 Asset 图标。 |

## 为新 Asset 添加双击与右键行为

```csharp
using Inno.Editor.Panel.FileBrowser;
using AssetIconKind = Inno.Platform.ImGui.ImGuiIcon;

[AssetEditor(typeof(AnimationClipAsset), useForChildren: true, priority: 100)]
public sealed class AnimationClipEditor : AssetEditor
{
    public override bool CanOpen(AssetEditorContext context) => true;

    public override void Open(AssetEditorContext context)
    {
        // Open the animation feature without changing File Browser identity.
    }

    public override AssetOperationValidation ValidateDelete(
        AssetEditorContext context)
        => context.info.status == AssetImportStatus.Ready
            ? AssetOperationValidation.valid
            : AssetOperationValidation.Invalid("The clip is not ready.");
}
```

Asset Rename/Delete 的物理事务始终由 `AssetManager` 执行。AssetEditor 只能验证以及接收提交后的通知，不能自行移动 source/meta/artifact，因此外部文件变化与 Editor 操作拥有同一身份规则。

Create Folder、Rename、Move 与 Delete 都接入共享中立 Undo/Redo。Rename/Move 只记录 source/target path；Delete 把 source、目录结构和 `.imeta` 编码进 History payload。大 payload 自动落到 `<Project>/Library/Editor/History`，Undo 先在临时目录完整验证 archive，再提交回 Asset root 并 `Rescan`，因此恢复失败不会留下半个目录。原 `.imeta` 会恢复相同 persistent ID；Redo 再走 `AssetManager.Delete`。目标发生外部冲突时操作失败并留在原栈，绝不覆盖新文件。Asset Browser selection 仅在文件系统事务成功后 best-effort 更新，通知异常不改变 History 结果。

## 文件与目录移动

Tree、List 和 Grid 使用同一个 `AssetFileEntry` 目录目标及 `panel/asset.file-browser` drop area。文件仍以共享 `AssetInfo` 作为 payload，目录以 `AssetFileEntry` 作为 payload；两者都可以拖到任意视图中的目录，因此可以从 Grid 拖到 Tree，也可以从 Tree 拖到 List/Grid。Tree 的 `Assets` 根节点和 Tree pane 未占用背景都明确以 Assets 根目录为目标；List/Grid 的未占用背景才以当前打开目录为目标。目标路径必须由每个 drop site 显式提供，不会隐式回退到当前目录。

Tree pane 只在名称或层级缩进真实超出 viewport 时产生横向范围，并显示原生水平 scrollbar；短内容没有 scrollbar。Tree 的 label/icon/hit area 只应用一次 `ScrollX`，不会出现内容比 disclosure 或 guide 多移动一份滚动距离的情况。

提交前统一检查目标目录存在、同名冲突、目录拖入自身或 descendant，以及 AssetEditor 对 move 的验证。拖到当前 parent 属于 no-op，不产生 History；成功移动后保留 source/meta identity、选择新路径，并以单个 `Move Asset` 操作进入 Undo/Redo。目录移动由 `AssetManager.Move` 原子处理，目录内子项不单独复制或逐项重建。SceneAsset 的 Rename、Move、拖放及其 Undo/Redo 只改变 Asset source metadata；已加载的 clean Scene 会在同一 UI frame 更新 document 路径和显示名，不产生 Hierarchy `*`。

所有 Tree/List/Grid 目录目标统一调用 `ImGuiWidget.DropTargetHighlight`。目标框使用全局 `DragDropTarget` 黄色、统一 rounding/thickness，并绘制在 viewport foreground draw list，因此不会被 Table column、Grid cell 或 child window 的 clip rect 截断。

为某类 Asset 添加额外右键菜单只需普通 Action：

```csharp
internal static class AnimationInteractionIds
{
    internal const string C_REIMPORT = "animation/reimport";
    internal const string C_FILE_BROWSER_AREA = "panel/asset.file-browser";
}

[EditorAction(AnimationInteractionIds.C_REIMPORT, AnimationInteractionIds.C_FILE_BROWSER_AREA)]
[EditorMenu(AnimationInteractionIds.C_FILE_BROWSER_AREA, "Animation/Reimport", order: 400)]
public sealed class ReimportAnimationAction : EditorAction<AssetFileEntry>
{
    protected override EditorActionState Query(
        EditorActionContext<AssetFileEntry> context)
        => context.target.extension == ".anim"
            ? EditorActionState.enabled
            : EditorActionState.hidden;

    protected override void Execute(EditorActionContext<AssetFileEntry> context)
    {
        AssetManager.Import(context.target.relativePath);
    }
}
```

同一个菜单 Attribute 自动出现在 Tree/List/Grid，因为三种视图都提交相同 area 和共享 `AssetFileEntry` target。

## 为 Asset 类型声明图标

图标扩展不要求 runtime Asset 程序集引用 Editor。在 Editor extension 项目中选择任意容器类型，并把任意数量的声明并排放在该类型上：

```csharp
using Inno.Editor.Panel.FileBrowser;

[AssetIcon(
    typeof(AnimationClipAsset),
    AssetIconKind.FileAudio,
    useForChildren: true,
    priority: 100)]
[AssetIcon(
    typeof(AnimationControllerAsset),
    AssetIconKind.DiagramProject)]
[AssetIcon(".animationclip", AssetIconKind.FileAudio)]
internal static class AnimationAssetIcons
{
}
```

容器类没有实例和运行时职责，只是 TypeCache 可以发现的声明位置。它可以是任意 class、struct、interface、enum 或 delegate；同一个类型允许多个 `AssetIcon`。内建声明位于 `Icons/BuiltInAssetIcons.cs`，不需要放在 `Properties`。

类型声明适合需要按照继承体系选择图标的 Editor extension；extension 声明适合引擎内建文件格式，并且不要求 FileBrowser 项目引用定义 Asset 类型的程序集。extension 可以省略开头的 `.`，匹配时忽略大小写，也支持 `.editor.cs` 这样的复合后缀。解析时先选择类型声明；没有类型声明时选择最长的匹配后缀，再用 priority 打破同等 specificity。

Host 代码使用普通 C# alias 将 `ImGuiIcon` 命名为 `AssetIconKind`；EditorScripts 则由 FileBrowser 项目的 `ScriptingApi.cs` 通过 `ScriptingApiExport(typeof(ImGuiIcon), "AssetIconKind", ...)` 获得相同的脚本侧名称。底层 `Inno.Platform.ImGui` 不声明 FileBrowser facade。两边都直接引用唯一的 `ImGuiIcon` 常量目录，因此没有第二份 enum、生成器或手写映射，新增底层 icon 会自动可用。

CLR 层的 icon 常量仍是 ImGui 所需的 `const string` glyph；`AssetIconKind.Xxx` 是 facade/catalog API，而不是另一个 runtime enum。标准 C# Attribute 参数不支持自定义 struct 常量，因此在“不生成重复 enum”的前提下这是唯一能够保持一比一目录和编译期常量的形式。业务声明不需要书写裸字符串。

内建 Text、Binary、Scene、Prefab 和 Scripting 图标全部在 `BuiltInAssetIcons` 上使用 extension overload 声明，没有基于具体 Asset CLR 类型的引用。FileBrowser 项目因此不再引用 `Inno.Assets.Types`、`Inno.Engine.Scene.Assets` 或 `Inno.Editor.Scripting`。内部 `AssetIconRegistry` 扫描当前 TypeCache snapshot 中的声明类型。EditorScripts 热重载时，新增或修改声明会随候选代际原子生效；移除声明或整个容器类型后，Registry 会释放旧映射并恢复优先级较低的内建声明，没有匹配时则使用通用 File icon。

`AssetEditorModule.GetIcon(entry)` 是唯一对外 presentation resolver，同时通过 `IInspectionIconProvider<AssetFileEntry>` 向 Inspection 基础设施提供同一个规则。File Browser 的三种视图与 Asset Inspection Header 都调用该入口，不复制 extension switch，也不各自持有 Registry snapshot。Registry 先按类型/extension 选中声明；若 declaration 字符串是已注册 Settings path，就直接读取其中的 `value`，否则把它当作 literal glyph。Settings 基础项目不提供 icon resolver。

## Rename 与打开

- 快速双击调用 AssetEditor 的 Open。
- Rename 只能从 entry 的右键菜单、F2 快捷键或 Create Folder 的创建完成流程启动；单击、延迟单击和双击都不会进入重命名。
- F2 会使用当前正在操作的 Tree、List 或 Grid 展示位置绘制输入框。
- Create Folder 完成后会选中新目录并自动进入重命名。
- Rename Action 自己持有输入/验证状态；Tree/List/Grid 只调用 `Present` 绘制 inline editor。
- 文件重命名只编辑最后一个扩展名前的真实名称，并在提交时无条件保留原文件的最后扩展名；目录名则完整可编辑。例如 `Player.iscene` 的输入值是 `Player`，`Tool.editor.cs` 的输入值是 `Tool.editor`。这里不为 `.editor.cs` 建立复合扩展特例，规则始终只是标准的最后扩展名 `.cs`。
- Tree/List/Grid 与 Hierarchy 共用 `ImGuiWidget.InlineRename` 的紧凑输入框；输入框采用相同 frame metrics，在 row 内垂直居中，首次获得焦点时全选内容，并绘制在 selection/hover highlight 之上。蓝色焦点线框以实际输入框为基准只向外扩展 1px，使用与 DropTarget 相同的统一 overlay 粗细并绘制到 foreground。List 不读取隐藏标签 Selectable 的临时 item 高度，而是以 Table `RowPosY1/RowPosY2` 的实际屏幕边界为居中基准。
- 输入框失去焦点或 selection 切换到其他 target 时，Rename Action 会提交当前有效名称并结束；无效名称保留原值并结束。
- Tree/List/Grid 的未占用背景收到左键点击时会清除当前 Asset selection。
- SceneAsset 打开 Action 由 Hierarchy feature 实现，但使用全局 Open 语义和共享路径参数，不形成 Panel project 引用。
- 全局 Save 保存尚无 source path 的 Scene 时，使用 File Browser 当前打开目录作为 fallback；已有 source 的 Scene 仍保存回自身路径。

ID 为 `asset-browser` 的 Asset Browser Module 只保存当前目录；Asset selection 属于当前 Editor session，不写入 `editor.ini`。运行期间若选中的路径被外部移动，Change Tracker 仍会按本次 session 的路径变化同步 selection；被删除时则清除。ID 为 `asset.file-browser` 的 Panel 自己保存 List/Grid 模式、搜索过滤、scope/type filter、Tree/Content 分隔比例、grid scale，以及 List 中 Name/Type/Source 两个分隔位置。没有 `[InnoEditor][Panel.asset.file-browser]` 状态时，Tree 与 Content 在扣除 splitter 后各占一半；拖动后以 `treePaneRatio` 的 `0..1` 归一化值保存，下次打开项目时恢复。列分隔位置同样以 `0..1` 的归一化值写入 `listNameSeparator` 和 `listTypeSeparator`，因此 Panel 宽度变化后仍能恢复相同比例。

List 的三个 column 使用同一个内容 inset，手动 splitter 只占用从 header 到最后一行的真实 table 高度，因此 header 与每一条内容 row 都能接收 resize 拖动，而下方空白区域不会继续接收 hover 或拖动。row Selectable 明确允许 splitter overlay 重叠，separator 不会吞掉 Name、Type 或 Source 的正常点击区域。Grid 图标和文件名使用 draw-list overlay 绘制，不通过 `SetCursorScreenPos` 移动布局 cursor；图标先从卡片中扣除顶部、水平和 label 间距，再按剩余区域等比缩小。最终位置使用 baked glyph 的 `X0/Y0/X1/Y1` 可见边界计算，所以 Font Awesome 中左右 bearing 不对称的 Cube、Folder 等图标也会把真实轮廓中心放在卡片水平中心线上，并且不会越过卡片上沿。Selectable 仍是唯一负责 cell 尺寸与输入的 ImGui item。Inline Rename 必须临时移动 cursor 时，会在恢复布局位置后提交零尺寸 item，避免扩展 parent boundary 的 ImGui assertion。

## Scripting API

EditorScripts 使用 `InnoEditor.Assets` 扩展 AssetEditor、声明 AssetIcon。Action/Menu/Drop Attribute 与运行时 API 共用 feature-owned `const string` ID；脚本必须显式写 `using InnoEditor.Assets;`。
