# Inno.Editor.ImGui

[Editor 索引](README.md) · [Platform ImGui](../platform/Inno.Platform.ImGui.md) · [Wiki 首页](../README.md)

`Inno.Editor.ImGui` 提供编辑器统一控件、菜单/拖放渲染桥和视觉配置。它只包装可复用的 UI 原语，不持有 Scene、Selection 或 Panel 业务状态。

```text
Inno.Editor.ImGui/
├─ Styling/
│  ├─ EditorPalette.cs
│  └─ EditorStyleMetrics.cs
├─ Renderers/
├─ Runtime/
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

Palette 与 Style Metrics 并列位于 `Styling`，但仍使用项目 namespace `Inno.Editor.ImGui`。所有 `ImGuiWidget.*.cs` 位于 `Widgets`，namespace 统一为 `Inno.Editor.ImGui.ImGuiWidget`；实现统一组成 `static partial ImGuiWidget`，可复用入口全部是 static 方法。Options、Result、presentation 与私有状态收口在对应的 `ImGuiWidget.<Feature>.cs` 中，不创建独立 Widget helper 文件。

## Palette 与 Style

所有主题颜色集中在 `EditorPalette`：原生 ImGui col、Inspector、Hierarchy、Asset Browser、Logging、轴颜色与 drag target 都不在 Panel 中声明。换主题只需替换这一个 palette surface。

所有跨 Panel 的像素布局、padding、spacing、rounding、列比例和最小尺寸集中在 `ImGuiWidget.style`（`EditorStyleMetrics`）。Panel 可以读取语义名，例如 `assetListNameSeparatorPosition`、`inspectorCardSpacing`、`hierarchyRenameMinimumWidth`，不应新增散落的固定像素。

`ImGuiWidget.SetupStyle()` 一次性把 layout metrics 和 `EditorPalette` 应用到原生 ImGui style。

## Menu renderer

`EditorMenuRenderer` 是唯一调用原生 `BeginMenu/MenuItem` 的业务渲染桥。它递归绘制任意层级的 `EditorMenuModel`，从 Action Attribute 自动读取快捷键标签，并把点击排入 Action queue。Panel 只提供 `EditorMenuContext(surface, target)`。

主菜单由同一模型生成，固定呈现为 `File`、`Edit`、`View`。`View` 的条目来自当前 `EditorPanelRegistry`，显示 checked 状态并调用内建 Toggle Panel Action；脚本代际新增或移除 Panel 时不需要修改菜单代码。标准 Panel window 不向原生 ImGui 提交 `p_open`，因此普通 Tab 完全不包含关闭按钮。当前可见 Panel 根据所属 Dock Node 的实际位置和尺寸，在 Dock Header 最右侧的原生 close slot 位置绘制一个独立关闭控件。控件会补偿图标在字体 slot 中的水平居中 inset，使 X 的可见右边缘与第一个 Tab 的可见左边缘使用相同的 `WindowBorderSize + FramePadding.X` 外边距。它不参与 Tab 排列、不绘制 Tab 背景，并与 Inspector card 删除按钮共用 `ImGuiIcon.Xmark`、文本颜色及 hover 颜色。点击只关闭当前选中的 Panel，不会关闭同一 Dock Node 内的其他 Tab。该实现不修改 cimgui 或 Dear ImGui 源码。

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
    trailingControlWidth: ImGuiWidget.GetIconButtonSize().X,
    drawContextMenu: DrawHeaderContextMenu);
```

`dimmed` 为 `true` 时，leading control、标题和 trailing control 使用与 Hierarchy inactive GameObject 相同的灰色文本色。GameBehavior 与 GameSystem Inspector 已把该参数绑定到各自的 `enabled` 状态。

Header 的 disclosure triangle 由 `DrawDisclosureIndicator` 统一绘制：保留 `▶ / ▼` glyph，并根据实际 header bounds 居中。卡片、disabled text 与 disclosure hover 颜色都来自 `EditorPalette`，便于主题统一替换。底层 TreeNode 仍负责 open state 和点击命中，因此没有第二套折叠状态。

`trailingControlWidth` 可以为多个右侧按钮预留固定宽度。Component 与 System Inspector 使用它放置 Move Up、Move Down 与 Remove。

`drawContextMenu` 在完整 Header TreeNode 仍是当前 ImGui item 时执行，因此右键命中覆盖整个 Header，而不会错误绑定到 enabled checkbox、标题或末尾按钮。Component、Transform 与 System 都使用相同入口。

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

其余常用控件包括 `SearchInput`、`BeginSearchPopup`/`EndSearchPopup`、`InlineRename`、`IconButton`、`CompactCheckbox`、`CenteredButton` 和 `CenteredProgressBar`。`CenteredProgressBar` 使用原生进度填充，但把 overlay 独立绘制在完整 bar 的几何中心，因此百分比不会跟随填充边缘移动。每个组件位于对应的 `ImGuiWidget.<Component>.cs`，避免继续形成一个混合所有控件的 EditorControls 文件。`GetGlyphVisualBounds` 与 `AddGlyphCentered` 使用 baked font 的 glyph bearing，而不是字符串 advance rectangle，适合把不对称 icon glyph 按实际可见轮廓居中。`InlineRename` 不缩放字体，通过 `inlineRenameFramePadding` 和 `inlineRenameVerticalInset` 在 Tree/Table/Grid 行内形成较矮且垂直居中的编辑框；其结果明确区分 Enter `Commit`、`FocusLost` 与 Escape `Cancel`，因此 feature 可以为校验失败定义一致的收尾规则。所有需要 identity 的控件都应传入稳定且在当前 ImGui scope 内唯一的 `id`。

## Tree 与拖拽反馈

`TreeNode` 统一负责整行 hit area、hover/selection 背景和树连接线。`TreeNodeResult.min/max` 表示整行几何，`contentMin` 表示排除层级缩进与箭头后的真实内容起点。Hierarchy 的 child-target 框使用 `contentMin..max`，因此不会覆盖左侧 Tree guide/indent 区域。面板仍可通过 ImGui style scope 控制行间距；Hierarchy 与 File Browser 当前统一使用 `2 px` 纵向间距。拖拽期间普通 hover 背景会暂停，调用方可用 `InsertionLine` 表示同级插入，或用 `DropTargetHighlight` 表示成为目标的 child。

Tree 行高至少采用原生 `TreeNode` frame 高度，不会因为某行只有文本而比包含按钮的行更薄。Hierarchy 与 File Browser 的行距通过命名 style metric 配置。`DropTargetHighlight` 绘制到当前 viewport 的 foreground draw list，因此目标框不会被发起它的 Panel clip rect 截断。

`IconText(..., highlight: true)` 保留下划线，并在 scope 内自动切换为 `Bold | Italic`；当前用于 active Scene 与 File Browser 当前目录。字体注册与自定义方式见 [Platform ImGui](../platform/Inno.Platform.ImGui.md#字体样式)。

`DragDropTarget(..., drawDefaultHighlight: false)` 可关闭 ImGui 默认目标框，适合需要按鼠标在行内位置绘制互斥反馈的复合目标。

`EditorDragDropRenderer` 只向 native payload 写入固定 session token；业务对象保留在 Interactions 的 managed session 中。Panel 与 Widget 的公开调用不要求 `unsafe`，FileBrowser 的 Grid 文本和搜索框也只使用安全的 Widget/ImGui API。
