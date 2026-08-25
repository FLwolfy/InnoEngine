# Inno.Editor.Panel.Inspector

[Editor 索引](README.md) · [Inspection](Inno.Editor.Inspection.md) · [Hierarchy](Inno.Editor.Panel.Hierarchy.md) · [ImGui](Inno.Editor.ImGui.md)

该项目拥有 Inspector Panel、统一 Target Header、Scene 的 Component/System 操作、动态 Add 菜单与引用拖放。可复用 Drawer 契约、Registry 和 serialized property renderer 已归入 `Inno.Editor.Inspection`。

Inspector 为所有可检查目标统一绘制无外部缝隙的 Target Header。Header 会抵消正文统一 content padding，背景与边框完整贴合 Inspector 顶部、左侧和右侧；内部仍使用自己的 header padding。Header 的大图标、名称、名称修改能力和第二行内容全部由当前 `InspectionDrawer<TTarget>` 提供；统一容器只负责布局、裁剪、边框和锁定。`BindName` 原子返回 `(name, setter)`；setter 为 `null` 时名称直接显示为文字，不绘制输入框。第二行严格限制为一行，适合放置 active、tag、路径、标签或其他轻量目标信息。

Target Header 右上角提供 lock/unlock 控件，其交互面积、图标居中与 hover 表现和 Panel Tab Bar 的关闭 X 使用同一套 compact icon widget。锁定只固定 Inspector 当前展示目标，不修改全局 Selection；Hierarchy 和 File Browser 可以继续选择其他对象，以便把它们拖到被锁定目标的属性上。锁定的 Scene 对象被销毁时会自动解锁，不保留失效引用。

Asset target Drawer 由 FileBrowser 项目自身提供，并通过 `IInspectionIconProvider<AssetFileEntry>` 复用 `AssetEditorModule` 的 type/extension icon registry；因此 File Browser Tree/List/Grid 与 Inspector Header 始终一致，EditorScripts 热重载图标声明后两处会同时更新。第二行 source path 使用与 File Browser 底部 breadcrumb 相同的半透明 palette color。

GameObject Header 的第二行包含 Active、项目 Tag picker 和 Layer picker。Tag 与 Layer 使用统一 `LabelChip` 几何和两种柔和 palette 背景，chip 与后方 selector 的间距固定为零，视觉结构与 Transform 轴前缀相同，但不会抢占输入控件的强调层级。Tag selector 的箭头区使用与原生 Layer Combo 相同的独立按钮背景和 down-arrow 几何。Tag picker 可以选择已有 Tag，也可以输入新 Tag 后按 Enter 或 Add；Add 的 `+` 使用完整 frame-height interaction area，glyph 与输入框垂直中心对齐。自定义 Tag 可以直接删除。删除 Tag 会把当前已加载对象上的对应值统一还原为 `Untagged`，Tag 定义与对象修改组成同一个 Undo/Redo 事务。项目 Tag 由 `SceneInspectionModule` 以 `tags=[...]` 保存到 `editor.ini` 的 `[InnoEditor][Module.scene-inspection]` section。Scene 中已经存在但尚未进入 catalog 的 Tag 会被自动同步。修改 Tag 通过 `SceneEdits.SetGameObjectTag` 进入 Undo/Redo，不是 Inspector 私有状态。Tag 的 Add/select 提示框使用与右键菜单一致的 auto-size popup，最小宽度取 selector、Add row 和最长 Tag row 的最大值，保证文字与删除按钮完整显示；同时明确设置 `NoScrollbar`/`NoScrollWithMouse`，因此不会产生滚动条或滚轮位移。

Layer picker 通过唯一内容 API `EditorSettings.Get("Project/Layers/Game Layers")` 取得 `EditorSettingObject`，再由本 feature 读取 names/masks 数组构造 `GameLayerStack`。关闭菜单时只显示名称，例如 `Default`；展开后选项显示 `(index) name`。未配置时使用定义的 Default-only 对象；已有对象引用未定义 slot 时发布 `GAMEOBJECT-LAYER-UNDEFINED`。修改对象 Layer 通过 `SceneEdits.SetGameObjectLayer` 记录稳定索引并进入 Undo/Redo，同时刷新 Scene layer query index。

`Edit/Settings... → Project/Layers` 直接渲染 `GameLayersSetting.OnDraw(EditorSettingObject)`。顶部使用低饱和 `Defined` LabelChip，并将 `current / 32` 绘制在无输入、无 focus 的只读 frame 中；两者作为一个复合标签零间距连接，计数框与 `Add layer...` 之间使用 `ImGuiStyle.ItemSpacing.X`，后者占用其余全部横向空间。界面只列出已经定义的 layer，以紧凑表格显示 slot、name 和 remove action；列之间绘制不可拖动的竖直分隔线，分隔线两侧统一使用可缩放的 `EditorStyleMetrics.cellPadding`。header、slot 编号以及只读 Default/Fixed 文字使用与可编辑 name field 相同的左侧 inset。自定义 name input 使用透明 frame background，从而与同一行 Slot cell 共享完全相同的 row background；Remove 使用与 File Browser breadcrumb 相同的无背景 `ClickableText` 表现，只在 hover/active 时改变文字颜色。未使用的 1–31 slot 收口在 `Add layer...` 选择器中，不再铺出三十一行空输入框。Enter 或编辑后失焦只更新 staged names/masks 数组，Apply 才写入 `<ProjectRoot>/EditorSettings.json`；该流程不创建 Asset、metadata、artifact 或 Game Layers 专属 History action。

Inspector Panel 关闭根 window padding，使外层纵向 scrollbar 贴紧 Dock body 边缘；所有 Target Header、卡片和 Drawer 正文统一放在 `ConstrainedContent` 中，由容器准确恢复一层标准 window padding，不再出现零间距或 Panel/child 双层空隙。该 auto-resize child 的显式 content width 始终等于 viewport 扣除左右 padding 后的宽度，并禁用自身 scrollbar/scroll input；因此 Inspector 在所有 target（包括 Behavior/System）下都不会产生横向 scroll range，也不需要逐帧重置 `scrollX`。长卡片标题会在右侧操作区之前裁剪，属性 label 和多轴数值字段会按真实可用宽度收缩，任何 Drawer 都不能把纵向滚动父级撑宽。

`GameLayerStack` 仍保留对称 interaction matrix API 与 source 数据，因为自定义物理、感知或查询系统可以显式调用 `CanInteract`/`SetInteraction`；当前引擎没有内建系统自动消费这些规则。因此 Inspector 不再显示 `Layer Interactions` 区域，项目只需要 layer 分类时无需配置它。

## Registry 扩展

```csharp
using System;

[InspectionDrawer(typeof(AnimationController))]
public sealed class AnimationControllerInspector
    : InspectionDrawer<AnimationController>
{
    public override string icon => ImGuiIcon.DiagramProject;

    protected override (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        AnimationController target)
        => (target.name, value => target.name = value);

    protected override string GetIcon(
        InspectionDrawContext context,
        AnimationController target)
        => target.hasErrors ? ImGuiIcon.TriangleExclamation : icon;

    protected override void DrawHeader(
        InspectionDrawContext context,
        AnimationController target)
    {
        ImGui.TextUnformatted($"States: {target.stateCount}");
    }

    protected override void Draw(
        InspectionDrawContext context,
        AnimationController target)
    {
    }
}

[PropertyDrawer(typeof(AnimationCurve))]
public sealed class AnimationCurveDrawer : IPropertyDrawer
{
    public void Draw(PropertyDrawContext context)
    {
    }
}
```

两个 Registry 位于 `Inno.Editor.Inspection`，均基于 `TypeRegistry`，随 TypeCache generation 原子刷新；构造或冲突失败不会发布半成品。Property 顺序按照字段/属性在脚本中的 metadata 顺序统一排序，不再强制 fields 在 properties 前。

## Area 与 Action

Component、System、EngineObject reference 和 Asset reference 分别使用 `panel/scene.inspector/component`、`panel/scene.inspector/system`、`panel/scene.inspector/engine-object-reference` 与 `panel/scene.inspector/asset-reference`。Add/Reset/Remove action 同样在 Attribute 和调用点直接使用 `inspector/...` 字符串 ID，不导出 `InspectorAreas` 或 `InspectorActions` facade。Add 菜单是动态 `EditorMenuSource`，每次从当前 TypeCache 发现可用类型；无需在 Inspector 主类中增加分支。

Component card 的上/下按钮改变附加顺序，Transform 保持置顶且不可移除。GameSystem 也可以上下移动和删除，但运行顺序仍由显式 `order` 决定。`enabled=false` 时 header 与 body 使用统一 dimmed 样式，body 保持可辨识但不可编辑。

Inspector 的可序列化属性、Component/System enabled、Add、Remove、Reset 与显示顺序全部通过 `SceneEdits` 记录中立历史。属性修改只编码对应 root property；元素操作保存 Stable Type ID、persistent ID、index 和该元素的属性数据。Undo 不会销毁并重建无关 Scene 对象，连续属性编辑才允许按 property merge key 合并。

## 引用拖放

Asset reference handler 接受共享 `AssetInfo`；EngineObject handler 接受当前 Scene 中的 `EngineObject`。Drawer 只提交 property target 和 area，具体兼容检查及赋值在 typed Drop handler 中完成。兼容的 Asset payload 悬停在 property control 上时使用全局 `DragDropTarget` palette 绘制黄色目标框；不兼容 payload 不显示可接受反馈。

## Scripting API

EditorScripts 使用 `InnoEditor.Inspection`，可声明 InspectionDrawer、PropertyDrawer 并使用 draw context。Facade 由 `Inno.Editor.Inspection` 提供；本项目只补充引用 drop target，Attribute 使用 feature-owned 字符串常量，运行时使用 typed area/command。具体内建 Panel、Registry snapshot 和内部 metadata cache 不导出。
