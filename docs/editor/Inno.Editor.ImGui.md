# Inno.Editor.ImGui

[Editor 索引](README.md) · [Platform ImGui](../platform/Inno.Platform.ImGui.md) · [Wiki 首页](../README.md)

`Inno.Editor.ImGui` 提供编辑器统一控件和视觉样式。它只包装可复用的 UI 原语，不持有 Scene、Selection 或 Panel 状态。

## CollapsingCard

```csharp
bool open = ImGuiWidget.CollapsingCard(
    id,
    title,
    drawLeadingControl: DrawEnabledToggle,
    drawTrailingControl: DrawRemoveButton,
    defaultOpen: true,
    dimmed: !enabled);
```

`dimmed` 为 `true` 时，leading control、标题和 trailing control 使用与 Hierarchy inactive GameObject 相同的灰色文本色。GameBehavior 与 GameSystem Inspector 已把该参数绑定到各自的 `enabled` 状态。

Header 的 disclosure triangle 由 `DrawDisclosureIndicator` 统一绘制：glyph 根据 header 高度精确垂直居中，hover 使用与 Log card disclosure button 相同的 ButtonHovered 反馈。底层 TreeNode 仍负责 open state 和点击命中，因此没有第二套折叠状态。

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

其余常用控件包括 `SearchInput`、`BeginSearchPopup`/`EndSearchPopup`、`InlineRename`、`IconButton`、`CompactCheckbox`、`CenteredButton`。所有需要 identity 的控件都应传入稳定且在当前 ImGui scope 内唯一的 `id`。

## Tree 与拖拽反馈

`TreeNode` 统一负责整行 hit area、hover/selection 背景和树连接线。`TreeNodeResult.min/max` 表示整行几何，`contentMin` 表示排除层级缩进与箭头后的真实内容起点。Hierarchy 的 child-target 框使用 `contentMin..max`，因此不会覆盖左侧 Tree guide/indent 区域。面板仍可通过 ImGui style scope 控制行间距；Hierarchy 与 File Browser 当前统一使用 `2 px` 纵向间距。拖拽期间普通 hover 背景会暂停，调用方可用 `InsertionLine` 表示同级插入，或用 `DropTargetHighlight` 表示成为目标的 child。

Tree 行高至少采用原生 `TreeNode` frame 高度，不会因为某行只有文本而比包含按钮的行更薄。`DropTargetHighlight` 绘制到当前 viewport 的 foreground draw list，因此目标框不会被发起它的 Panel clip rect 截断。

`IconText(..., highlight: true)` 保留下划线，并在 scope 内自动切换为 `Bold | Italic`；当前用于 active Scene 与 File Browser 当前目录。字体注册与自定义方式见 [Platform ImGui](../platform/Inno.Platform.ImGui.md#字体样式)。

`DragDropTarget(..., drawDefaultHighlight: false)` 可关闭 ImGui 默认目标框，适合需要按鼠标在行内位置绘制互斥反馈的复合目标。

`DragDropSource<TPayload>` 与 `DragDropTarget<TPayload>` 的公开调用同样不要求 `unsafe`；`TPayload` 只需满足 `unmanaged`。指针拷贝被限制在 Widget 的 private native helper 中。
