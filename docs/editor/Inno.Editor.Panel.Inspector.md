# Inno.Editor.Panel.Inspector

[Editor 索引](README.md) · [Inspection](Inno.Editor.Inspection.md) · [Hierarchy](Inno.Editor.Panel.Hierarchy.md) · [ImGui](Inno.Editor.ImGui.md)

该项目拥有 Inspector Panel、统一 Target Header、Scene 的 Component/System 操作、动态 Add 菜单与引用拖放。可复用 Drawer 契约、Registry 和 serialized property renderer 已归入 `Inno.Editor.Inspection`。

Inspector 为所有可检查目标统一绘制无外部缝隙的 Target Header。Header 会抵消正文统一 content padding，背景与边框完整贴合 Inspector 顶部、左侧和右侧；内部仍使用自己的 header padding。Header 的大图标、名称、名称修改能力和第二行内容全部由当前 `InspectionDrawer<TTarget>` 提供；统一容器只负责布局、裁剪、边框和锁定。`BindName` 原子返回 `(name, setter)`；setter 为 `null` 时名称直接显示为文字，不绘制输入框。第二行严格限制为一行，适合放置 active、tag、路径、标签或其他轻量目标信息。

Target Header 右上角提供 lock/unlock 控件，其交互面积、图标居中与 hover 表现和 Panel Tab Bar 的关闭 X 使用同一套 compact icon widget。锁定只固定 Inspector 当前展示目标，不修改全局 Selection；Hierarchy 和 File Browser 可以继续选择其他对象，以便把它们拖到被锁定目标的属性上。Scene identity 只以 persistent ID 保留并从当前 Edit/Play Session 重新解析，因此 assembly reload 后会指向 replacement 或 Missing placeholder；没有稳定 identity 的 collectible target 只保留弱引用。锁定目标被销毁、移除或无法重解析时自动解锁，不会固定退休 Plugin/Script ALC。

Asset target Drawer 由 FileBrowser 项目自身提供，并通过 `IInspectionIconProvider<AssetFileEntry>` 复用 `AssetEditorModule` 的 type/extension icon registry；因此 File Browser Tree/List/Grid 与 Inspector Header 始终一致，EditorScripts 热重载图标声明后两处会同时更新。第二行 source path 使用与 File Browser 底部 breadcrumb 相同的半透明 palette color。Plugin Source Mount 根使用 `IPlugin` 类型，不伪装成普通 Directory，也不创建 `.iplugin` companion asset。

GameObject Header 的第二行包含 Active、项目 Tag picker 和 Layer picker。`SceneProjectSettingsModule` 按 `ProjectSettingsStore.revision` 刷新隔离的 `GameTagCatalog` 与 `GameLayerStack` 快照；Inspector 不从 `editor.ini` 或 `EditorSettings.inno` 建立第二份 catalog。对象修改通过 `SceneEdits` 进入 Scene History。

定义在 `Edit/Settings... → Project/Scene/Tags` 与 `Project/Scene/Layers` 编辑，分别由 `ProjectSettingEditor<GameTagCatalog>` 与 `ProjectSettingEditor<GameLayerStack>` 暂存，并由 Settings 窗口右下角的单一 `Apply` 写入 `<ProjectRoot>/ProjectSettings.inno`。删除定义不会自动重写已加载或未加载 Scene；assignment 仍保存在 Scene/Prefab 中，并发布 `GAMEOBJECT-TAG-UNDEFINED` 或 `GAMEOBJECT-LAYER-UNDEFINED`，直到用户恢复定义或显式修改对象。这避免一次设置操作隐式制造大量 Scene dirty state。

Layer 页面以紧凑表格显示 slot、globally stable ID、name 与 remove action；未使用 slot 收口到 `Add layer...`。Tag 页面提供统一 Add 与定义列表，并与 Layer 表格共享相同的 cell padding、plain-cell frame inset、header background、Action 列宽和 inner borders。GameObject Header 的 Layer/Tag selector 与 Layer 添加 selector 都使用共享 menu popup contract，具有与右键菜单一致的 padding、颜色和 work-area-bounded 滚动行为。Apply 时两者分别由协议 Composer 捕获 sparse layer/interaction operations 与 tag additions/removals，所以多个 Plugin 可以修改同一设置而不互相覆盖整个集合。两者只是普通强类型 Project Setting Drawer，不创建 Asset、metadata 或 feature 专属持久化通道。

Inspector Panel 关闭根 window padding，使外层纵向 scrollbar 贴紧 Dock body 边缘；所有 Target Header、卡片和 Drawer 正文统一放在 `ConstrainedContent` 中，由容器准确恢复一层标准 window padding，不再出现零间距或 Panel/child 双层空隙。该 auto-resize child 的显式 content width 始终等于 viewport 扣除左右 padding 后的宽度，并禁用自身 scrollbar/scroll input；因此 Inspector 在所有 target（包括 GameBehavior/GameSystem）下都不会产生横向 scroll range，也不需要逐帧重置 `scrollX`。长卡片标题会在右侧操作区之前裁剪，属性 label 和多轴数值字段会按真实可用宽度收缩，任何 Drawer 都不能把纵向滚动父级撑宽。

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

Component card 的上/下按钮改变附加顺序，Transform 保持置顶且不可移除。Project Script 以及 Plugin 提供的 Renderer、Camera、Light 都直接继承唯一的 `GameBehavior`，并在 card header 使用同一个 enabled checkbox；继承的隐藏序列化属性不会再次出现在 body。GameSystem 也可以上下移动和删除，但运行顺序仍由显式 `order` 决定。`enabled=false` 时 header 与 body 使用统一 dimmed 样式，body 保持可辨识但不可编辑。

展开的 GameBehavior/GameSystem 只有在存在可显示序列化属性或 Missing 状态说明时才创建 `CardBody`。没有正文的类型只绘制 Header，正文高度严格为零，不保留空背景、边框或 padding；卡片之间的标准外部间距保持不变。

Inspector 的可序列化属性、Component/System enabled、Add、Remove、Reset 与显示顺序全部通过 `SceneEdits` 记录中立历史。属性修改只编码对应 root property；元素操作保存 Stable Type ID、persistent ID、index 和该元素的属性数据。Undo 不会销毁并重建无关 Scene 对象，连续属性编辑才允许按 property merge key 合并。

Play Scene 提交后，Selection 会按 persistent ID 从 Edit 对象映射到 runtime copy，Inspector 因而直接展示并编辑正在运行的 Component、System 与 Transform。所有修改仍走同一个 `SceneEdits` API，但 History owner 已切换到隔离 Play 分支；停止时 runtime 修改和该分支一并释放，Edit 对象与 Edit Undo/Redo 原样恢复。

## 引用拖放

Asset reference handler 接受共享 `AssetInfo`；EngineObject handler 接受当前 Scene 中的 `EngineObject`。Drawer 只提交 property target 和 area，具体兼容检查及赋值在 typed Drop handler 中完成。兼容的 Asset payload 悬停在 property control 上时使用全局 `DragDropTarget` palette 绘制黄色目标框；不兼容 payload 不显示可接受反馈。

Asset reference Combo 的可见身份固定为 `source-id:asset-name`，例如
`inno.rendering.2d:DefaultSprite`；project mount 使用 `project:`。目录和扩展名不占用字段或菜单宽度，
但每个候选 hover 会显示完整 canonical `AssetPath`，搜索同时匹配短身份与完整路径。
Selectable 的内部 ImGui ID 使用 Persistent ID，因此两个目录中同名资产不会发生交互冲突。

## Scripting API

EditorScripts 使用 `InnoEditor.Inspection`，可声明 InspectionDrawer、PropertyDrawer 并使用 draw context。Facade 由 `Inno.Editor.Inspection` 提供；本项目只补充引用 drop target，Attribute 与运行时 API 共用项目根目录 `InspectorInteractionIds` 中的 `const string`。具体内建 Panel、Registry snapshot 和内部 metadata cache 不导出。
