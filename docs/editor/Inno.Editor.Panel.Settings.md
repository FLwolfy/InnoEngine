# Inno.Editor.Panel.Settings

[Editor 索引](README.md) · [Settings API](Inno.Editor.Settings.md) · [Global feature](Inno.Editor.Panel.Global.md) · [Wiki 首页](../README.md)

`Inno.Editor.Panel.Settings` 是 `Inno.Editor.Settings` 的内建 ImGui frontend。窗口使用 Modal 背景变暗和输入阻塞，同时保留普通窗口的移动、边缘拉伸与尺寸约束；它不能 Dock、Collapse 或最小化。

## 窗口与生命周期

- 主菜单 `Edit/Settings...` 的 Attribute 使用 feature-owned `const string` ID `editor.settings.open`，运行时执行对应的 `EditorCommand`。
- 初始逻辑尺寸 `1050 × 700`，最小尺寸 `760 × 520`，随全局 zoom 缩放。
- Window flags 包含 `NoDocking | NoCollapse`；淡入、显示和淡出期间阻止其他 Editor 区域交互。
- 左右 pane 默认按 `1:3` 分配；splitter 使用 `0..1` 比例并只为两侧各保留一个很小的可见宽度，因此可以像 File Browser 一样几乎拖到边缘。交互区域保持易拖动宽度，视觉只绘制居中的细线。
- 底部只有 Apply 与 Cancel。没有 staged 修改时 Apply 禁用；提交后保持窗口打开并重新禁用，Cancel 丢弃当前 staged 对象。
- Catalog revision 改变时，窗口从当前 definitions 重建页面树和 staged session。

## 左侧 Tree

搜索框固定在左侧顶部。Panel 从 `EditorSettings.definitions` 在本地合成不可变页面树；合成节点只是 frontend state，不是 Settings 项目的公开类型。

- Settings Tree 复用 `ImGuiWidget.TreeNode` 的全行 selection、hover、展开状态和层级连接线，但不绘制 icon。
- Tree widget 自己提交完整行的 hit area，Modal popup 不会吞掉节点点击；label、整行和叶节点都可以选择。
- 搜索匹配 page label/path/description，以及后代 field 的 label/section/description；命中 field 时选择所属 page 并展开祖先。
- sibling 按字母排序，field 的最后一个路径段不进入 Tree。

## 右侧 Overview

没有显式 page definition 的合成页面显示直属子页面。存在 page definition 时先显示其 description；没有直属 field 时同样列出直属子页面。入口使用 `ImGuiWidget.HoverText`：

- 默认只绘制强调色文字，不提交 `Selectable` 或按钮背景；
- hover 时改变文字颜色、显示手型 cursor 和 underline；
- 点击只导航到子 page；子 page description 不占用正文布局，只在链接 label hover 时显示 tooltip。

扩展只需声明 path 和 description：

```csharp
[EditorSettingPath("Global/Appearance")]
public sealed class AppearanceSettingsPage : EditorSetting
{
    public override string description => "Customize editor appearance.";
}
```

## 右侧 Field

存在直属 field 时：

1. 非空 `section` 按字母排序并使用 `SeparatorText`。
2. 最后一个路径段作为左侧 label，右侧是 frontend 管理的 content container。
3. container 把该字段的 staged `EditorSettingObject` 传给 `EditorSetting.Draw`。
4. 每个 field 都附带 Reset；当前值等于注册默认值时按钮禁用。每个 page 顶部也始终有 Reset，它递归重置该 page 的直属与所有后代 field，并在全部字段均为默认值时禁用。所有 Reset column 都在按钮左侧保留统一的 `ImGuiStyle.ItemSpacing.X`，不会与伸展内容控件贴合。
5. field description 只在左侧 label hover 时显示有最小宽度和最大换行宽度的 tooltip，不会退化成单字符竖列。
6. content 使用 stretch column，占满 label 与固定宽度 Reset column 之间的全部空间。
7. 多行 `OnDraw` 仍由同一个 group 包裹，字段不需要计算 label 间距或换行位置。

右侧顶部只显示当前 page 自己的 description，并在其下保留更明显的段落间距；合成父页面和子页面的说明不会重复铺在当前正文中。

```csharp
[EditorSettingPath("Global/Scene/Grid/Visible")]
public sealed class GridVisibilitySetting : EditorSetting
{
    public override EditorSettingObject defaultValue => CreateDefault();

    public override string section => "Grid";

    public override string description => "Shows the editing grid.";

    protected override void OnDraw(EditorSettingObject setting)
    {
        bool value = setting.GetAsBoolean("value", true);
        if (NativeImGui.Checkbox("##visible", ref value))
            setting.SetAsBoolean("value", value);
    }

    private static EditorSettingObject CreateDefault()
    {
        var value = new EditorSettingObject();
        value.SetAsBoolean("value", true);
        return value;
    }
}
```

Settings frontend 不提供 DrawBoolean、DrawIcon、DrawChoice 或其他内建字段控件。字段属于 feature，因而直接使用 feature 选择的 UI API；frontend 只拥有容器、label、description、递归 Reset 和 staged object 生命周期。

## Staging 与提交

内部 `SettingsEditSession` 从 definitions 与 `EditorSettings.Get(path)` 创建每个字段的独立对象。`EditorSetting.Draw` 为每个 staged object 保留弱引用 baseline，并返回当前对象是否偏离首次绘制值；因此手工改回原值后 Apply 会重新禁用。编辑只改 staged 对象；Apply 才调用一次 `EditorSettings.Apply`，将全部值和 Reset 路径作为一个原子提交写入 `<ProjectRoot>/EditorSettings.json`。

每次产生实际变化的提交只形成一条共享 `Apply Settings` History entry。撤销或重做会恢复整个设置文档并发布一次 `EditorSettings.changed(settings)`。Game Layers、Icons 和 Actual Size 没有自己的 Settings Undo/Redo action。

Modal、左右 pane、Tree child、field table、disabled scope 与 tooltip 的 ImGui Begin/End 均由 `try/finally` 配对。某个 Settings 扩展的 `OnDraw` 抛异常时，不会污染 Modal 或下一帧的 ImGui stack。

## 项目边界

该项目没有公开类型，只实现 Settings frontend。业务 feature 只引用 [Inno.Editor.Settings](Inno.Editor.Settings.md)，不引用本 Panel。`SettingsWindowModule` 拥有 Modal 状态、局部页面树和 edit session；关闭窗口和 Editor Stop 都释放临时状态。

[上一页：Inno.Editor.Settings](Inno.Editor.Settings.md) · [下一页：Inno.Editor.Panel.Stats](Inno.Editor.Panel.Stats.md)
