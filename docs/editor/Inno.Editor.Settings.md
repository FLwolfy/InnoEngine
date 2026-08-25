# Inno.Editor.Settings

[Editor 索引](README.md) · [Settings 界面](Inno.Editor.Panel.Settings.md) · [Global feature](Inno.Editor.Panel.Global.md) · [Wiki 首页](../README.md)

`Inno.Editor.Settings` 是路径驱动、与 UI 后端无关的项目设置内核。它只公开设置定义、JSON 对象、路径 Attribute 和项目服务，不提供图标解析器、ImGui 控件、内建字段类型或 feature 默认值。

## 当前稳定模型

```text
[EditorSettingPath("Global/Appearance/Icons/Scene")]
                         │
                         ├─ override OnDraw → Icons 页面中的 Scene field
                         └─ 默认 OnDraw     → 完整路径对应的 overview page
                                              │
                                              ▼
                               EditorSettings.Get(path)
                                              │
                                              ▼
                                  EditorSettingObject
                                              │
                                              ▼
                           <ProjectRoot>/EditorSettings.json
```

- 完整路径同时是注册身份、读取地址和磁盘 key；不存在独立的 path、area、scope、provider 或 ID 类型。
- 缺失的祖先节点由 frontend 从 definitions 自动合成，父节点无需中央注册。
- 没有 override `OnDraw` 的 `EditorSetting` 是 page definition；`description` 是页面说明。
- override `OnDraw` 的 `EditorSetting` 是 field；最后一个路径段是 label，父路径是所属页面。
- `section` 的公开签名是非空 `string`；不分组的定义保留基类内部的空实现，非空 section 由 frontend 按不区分大小写的字母顺序绘制。
- 所有值只写入项目根目录的 `EditorSettings.json`，不进入 `Settings/`、`Assets`、AssetManager、`editor.ini` 或用户目录。

## 定义字段

每个字段以 `EditorSettingObject` 声明默认对象，并直接用 GetAs/SetAs 方法绘制自己的内容：

```csharp
using Inno.Editor.Settings;

[EditorSettingPath("Global/Appearance/Icons/Scene")]
public sealed class SceneIconSetting : EditorSetting
{
    public override EditorSettingObject defaultValue => CreateDefault();

    public override string section => "Editor Icons";

    public override string description => "Selects the Scene glyph.";

    protected override void OnDraw(EditorSettingObject setting)
    {
        string value = setting.GetAsString("value", "default-glyph")!;
        if (DrawSceneIconPicker(ref value))
            setting.SetAsString("value", value);
    }

    private static EditorSettingObject CreateDefault()
    {
        var value = new EditorSettingObject();
        value.SetAsString("value", "default-glyph");
        return value;
    }
}
```

`OnDraw` 不接收 framework draw context。字段可以直接使用所属 feature 已经依赖的 UI API；Settings 内核不会因此依赖 ImGui。页面只需要 description，不需要空绘制函数：

```csharp
[EditorSettingPath("Global/Appearance")]
public sealed class AppearanceSettingsPage : EditorSetting
{
    public override string description
        => "Customize editor appearance and semantic presentation.";
}
```

## 最小公开 API

| API | 当前语义 |
| --- | --- |
| `EditorSettingPathAttribute` | 用一个原始字符串声明完整路径，并用 `order` 调整同组字段的稳定顺序。 |
| `EditorSetting` | 表示 page 或 field；只有无参数构造，公开 placement metadata、`defaultValue`、`IsDefault` 和 `Draw`，protected virtual `OnDraw` 是唯一绘制扩展点；`Draw` 返回 staged object 是否发生实际变化。 |
| `EditorSettingObject` | 隔离的 JSON object；公开 API 只有构造函数与 GetAs/SetAs 基元、数组方法。 |
| `EditorSettings` | 发现定义、读取有效对象、原子 Apply，并通过统一 Editor History 支持 Undo/Redo。 |

`EditorSettingObject` 支持 Boolean、Int32、UInt32、Int64、UInt64、Single、Double、String，以及 Boolean、Int32、UInt32、Single、Double、String 数组。数组读取始终返回独立副本；Single/Double 拒绝非有限值。框架不公开任意泛型反序列化入口，也不允许字段把运行时对象、`Type` 或 delegate 放入设置值。

唯一的业务读取入口是：

```csharp
EditorSettingObject value = editorSettings.Get("Global/Appearance/Icons/Scene");
string glyph = value.GetAsString("value", "default-glyph")!;
```

路径无效、没有定义或指向 page 时，`Get` 抛出 `ArgumentException`。返回值始终隔离；调用方修改它不会改变已提交文档。

## 存储、Apply 与 History

磁盘中的每个 property name 是完整路径，每个 property value 必须是 JSON object：

```json
{
  "Global/Appearance/Accessibility/Actual Size": {
    "value": 1.25
  },
  "Project/Layers/Game Layers": {
    "names": ["Default", "Player"],
    "interactionMasks": [4294967295]
  }
}
```

`Apply(values, resets)` 接受原始字符串路径。一次有效 Apply 先生成完整 replacement，通过同目录临时文件原子覆盖根目录文档，再向 `EditorInteractions.history` 记录一条 `Apply Settings`。这条记录保存 Apply 前后的完整设置文档，因此 Actual Size、Icons、Game Layers 以及未来字段共用同一种 Undo/Redo 协议，不存在 feature 专属 Settings action。

Undo/Redo 使用同一个严格 writer 恢复完整文档。磁盘写入或 History 记录失败时，Apply 回滚到原文档；无实际变化的 Apply 返回 `false` 且不制造空 History entry。成功的 Apply、Undo 或 Redo 都只调用一次：

```csharp
editorSettings.changed += settings => RefreshFrom(settings);
```

事件参数就是已提交的 `EditorSettings` 服务，没有 change-event args 或路径 diff 类型。

Reset 通过 `resets` 集合删除指定完整路径的 override，随后 `Get` 返回该定义的默认对象。字段必须 override `defaultValue` 并在每次访问时返回新对象；page 保留基类的内部空实现。`IsDefault` 使用注册时绑定的隔离副本比较暂存值。它与普通值修改处于同一 Apply、同一原子写入和同一 History entry 中。

## Feature 所有权

框架不拥有任何内建设置：

- [Inno.Editor.Panel.Global](Inno.Editor.Panel.Global.md) 定义 Global/Appearance 页面、Actual Size 和 Scene/GameObject/Prefab/Layers/Folder/File icon 字段，并拥有临时 Zoom actions/module。
- [Inno.Editor.Panel.Inspector](Inno.Editor.Panel.Inspector.md) 定义 Project/Layers 与 Game Layers 字段。
- Hierarchy、Inspector、FileBrowser 和 Global zoom module 直接用原始路径调用 `EditorSettings.Get`，各自解释对象属性；没有 `EditorIcons` 常量类或 `IEditorIconResolver` 中间层。
- Game Layers 以 names/masks 数组保存在同一个根目录文档中，不存在 `.ilayers` importer、AssetObject、metadata、Asset History action 或第二套 Undo 栈。

## 热重载与 Scripting API

每个定义必须有可构造的无参数构造函数。TypeCache generation 激活时，Catalog 先构造完整候选，再原子切换 definitions snapshot；`catalogRevision` 标识当前发现结果。

EditorScripts 使用 `using InnoEditor.Settings;`。脚本导出严格限于 `EditorSettingPathAttribute`、`EditorSetting`、`EditorSettingObject` 和 `EditorSettings`，其定义、读取、绘制和 Apply 行为与 Host 完全一致。

## 常见约束

- `OnDraw` 只改传入的 staged object，不直接写文件、不调用 Apply。
- page 不 override `defaultValue` 或 `OnDraw`；field 必须同时 override `defaultValue` 与 `OnDraw`。
- field 至少包含一个 parent page segment；完整 field path 不能同时充当其他定义的 parent page。
- 不要把 Selection、Undo payload、dirty Scene、Dock layout 或 Workspace 导航写入 `EditorSettings.json`。
- 删除或移动 path 时同步当前项目 JSON、调用方、测试和 Wiki，不保留旧 key alias。

[上一页：Inno.Editor.Scene](Inno.Editor.Scene.md) · [下一页：Inno.Editor.Panel.Settings](Inno.Editor.Panel.Settings.md)
