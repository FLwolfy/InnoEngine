# Inno.Editor.ImGui

[Editor 索引](README.md) · [Platform ImGui](../platform/Inno.Platform.ImGui.md) · [Wiki 首页](../README.md)

`Inno.Editor.ImGui` 提供编辑器统一控件、菜单/拖放渲染桥和视觉配置。它只包装可复用的 UI 原语，不持有 Scene、Selection 或 Panel 业务状态。

```text
Inno.Editor.ImGui/
├─ EditorPalette.cs
├─ EditorStyleMetrics.cs
├─ Renderers/
└─ Widgets/
   ├─ ImGuiWidget.Style.cs
   ├─ ImGuiWidget.Search.cs
   ├─ ImGuiWidget.ContextMenu.cs
   ├─ ImGuiWidget.InlineRename.cs
   ├─ ImGuiWidget.Controls.cs
   ├─ ImGuiWidget.Card.cs
   ├─ ImGuiWidget.Tree.cs
   └─ ...
```

Palette 与 Style Metrics 并列位于项目根目录。所有 `ImGuiWidget.*.cs` 位于 `Widgets`，namespace 统一为 `Inno.Editor.ImGui.Widgets`；实现统一组成 `static partial ImGuiWidget`，可复用入口全部是 static 方法。Options、Result 等纯数据值类型也位于同一 Widgets namespace。

## Palette 与 Style

所有主题颜色集中在 `EditorPalette`：原生 ImGui col、Inspector、Hierarchy、Asset Browser、Logging、轴颜色与 drag target 都不在 Panel 中声明。换主题只需替换这一个 palette surface。

所有跨 Panel 的像素布局、padding、spacing、rounding、列宽和最小尺寸集中在 `ImGuiWidget.style`（`EditorStyleMetrics`）。Panel 可以读取语义名，例如 `assetListNameColumnWidth`、`inspectorCardSpacing`、`hierarchyRenameMinimumWidth`，不应新增散落的固定像素。

`ImGuiWidget.SetupStyle()` 一次性把 layout metrics 和 `EditorPalette` 应用到原生 ImGui style。

## Menu renderer

`EditorMenuRenderer` 是唯一调用原生 `BeginMenu/MenuItem` 的业务渲染桥。它递归绘制任意层级的 `EditorMenuModel`，从 Action Attribute 自动读取快捷键标签，并把点击排入 Action queue。Panel 只提供 `EditorMenuContext(surface, target)`。

`ContextMenu` 绑定最近提交的 ImGui item；`WindowContextMenu` 只响应当前 window 中没有 item 占用的背景区域。两者都会先构建菜单模型，模型没有可见条目时不会打开原生 popup，因此不会显示空的黑色菜单框。

所有 context menu 在 `BeginContextMenu` / `EndContextMenu` 范围内应用同一组 `EditorPalette.menu*` 颜色和 `EditorStyleMetrics.menu*` padding、spacing、rounding 与 border。Panel 的局部 Table/Tree style 不会再改变 popup 外观。Popup 打开时，Tree、disclosure 等自绘控件会暂停其底层 hover feedback；原生 popup 本身接收鼠标事件，避免 hover 或点击继续影响菜单后面的 entry。

## CollapsingCard

```csharp
bool open = ImGuiWidget.CollapsingCard(
    id,
    title,
    drawLeadingControl: DrawEnabledToggle,
    drawTrailingControl: DrawRemoveButton,
    defaultOpen: true,
    dimmed: !enabled,
    trailingControlWidth: ImGuiWidget.GetIconButtonSize().X);
```

`dimmed` 为 `true` 时，leading control、标题和 trailing control 使用与 Hierarchy inactive GameObject 相同的灰色文本色。GameBehavior 与 GameSystem Inspector 已把该参数绑定到各自的 `enabled` 状态。

Header 的 disclosure triangle 由 `DrawDisclosureIndicator` 统一绘制：保留 `▶ / ▼` glyph，并根据实际 header bounds 居中。卡片、disabled text 与 disclosure hover 颜色都来自 `EditorPalette`，便于主题统一替换。底层 TreeNode 仍负责 open state 和点击命中，因此没有第二套折叠状态。

`trailingControlWidth` 可以为多个右侧按钮预留固定宽度。Component 与 System Inspector 使用它放置 Move Up、Move Down 与 Remove。

展开内容使用配套的 `CardBody` 绘制：

```csharp
if (open)
{
    ImGuiWidget.CardBody(
        id,
        drawContent: DrawSerializedProperties,
        dimmed: !enabled);
    NativeImGui.TreePop();
}
```

`CardBody` 提供统一的背景、边框与内边距；`dimmed` 为 `true` 时，正文整体灰化且不可编辑，但 header 中的 enabled checkbox 仍可用于重新启用对象。相邻卡片之间的外部间距由调用方控制。

其余常用控件包括 `SearchInput`、`BeginSearchPopup`/`EndSearchPopup`、`InlineRename`、`IconButton`、`CompactCheckbox`、`CenteredButton`。每个组件位于对应的 `ImGuiWidget.<Component>.cs`，避免继续形成一个混合所有控件的 EditorControls 文件。`InlineRename` 不缩放字体，通过 `inlineRenameFramePadding` 和 `inlineRenameVerticalInset` 在 Tree/Table/Grid 行内形成较矮且垂直居中的编辑框。所有需要 identity 的控件都应传入稳定且在当前 ImGui scope 内唯一的 `id`。

## Tree 与拖拽反馈

`TreeNode` 统一负责整行 hit area、hover/selection 背景和树连接线。`TreeNodeResult.min/max` 表示整行几何，`contentMin` 表示排除层级缩进与箭头后的真实内容起点。Hierarchy 的 child-target 框使用 `contentMin..max`，因此不会覆盖左侧 Tree guide/indent 区域。面板仍可通过 ImGui style scope 控制行间距；Hierarchy 与 File Browser 当前统一使用 `2 px` 纵向间距。拖拽期间普通 hover 背景会暂停，调用方可用 `InsertionLine` 表示同级插入，或用 `DropTargetHighlight` 表示成为目标的 child。

Tree 行高至少采用原生 `TreeNode` frame 高度，不会因为某行只有文本而比包含按钮的行更薄。Hierarchy 与 File Browser 的行距通过命名 style metric 配置。`DropTargetHighlight` 绘制到当前 viewport 的 foreground draw list，因此目标框不会被发起它的 Panel clip rect 截断。

`IconText(..., highlight: true)` 保留下划线，并在 scope 内自动切换为 `Bold | Italic`；当前用于 active Scene 与 File Browser 当前目录。字体注册与自定义方式见 [Platform ImGui](../platform/Inno.Platform.ImGui.md#字体样式)。

`DragDropTarget(..., drawDefaultHighlight: false)` 可关闭 ImGui 默认目标框，适合需要按鼠标在行内位置绘制互斥反馈的复合目标。

`DragDropSource<TPayload>` 与 `DragDropTarget<TPayload>` 的公开调用同样不要求 `unsafe`；`TPayload` 只需满足 `unmanaged`。指针拷贝被限制在 Widget 的 private native helper 中。
