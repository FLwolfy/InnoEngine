# Inno.Editor.Annotations

[Editor 索引](README.md) · [Inspection](Inno.Editor.Inspection.md) · [Wiki 首页](../README.md)

## 职责与依赖

独立、无状态的 Inspector 创作标注契约。它只依赖 System.Attribute 与 Scripting.Api 导出声明，不引用 Serialization、Scene、Rendering、ImGui 或 Panel。Inspection 负责解释及渲染；Core.Serialization 只负责数据。不存在旧 namespace alias。

脚本入口为 `using InnoEditor.Annotations;`，引擎 Editor 扩展使用 `Inno.Editor.Annotations`。无需初始化；所有类型以 `ScriptingApiScope.Authoring` 导出。创作编译保留 `INNO_EDITOR` metadata；Player 编译在绑定目标平台 DLL 前移除标注、自定义派生标注声明与相关 using，最终无该程序集引用。

## 公开 API

所有标注继承 `InspectorPresentationAttribute`，共有可写 `order` 排序属性。

| 类型/构造 | 属性与语义 |
| --- | --- |
| `Header(string title, string description = "")` | `title` 分组标题，`description` 仅悬停可见。 |
| `Text(string text)` | `text` 常驻中性说明。 |
| `Space(float height = 8)` | `height` 逻辑像素间距。 |
| `Tooltip(string text)` | `text` 属性行悬停说明。 |
| `InspectorName(string name)` | `name` 显示名称，不改变持久键。 |
| `Range(double minimum, double maximum)` | 数值编辑范围，不改变运行时值校验。 |
| `InspectorReadOnly()` | 禁用字段编辑。 |
| `ShowIf` / `HideIf` | `(string memberName)`、`(string memberName, object expectedValue)`、`(string memberName, InspectorCondition condition)`；公开 `memberName`、`condition`、`expectedValue`。 |
| `HelpBox` | `text`、`messageType`、`conditionMember`、`condition`、`expectedValue`；支持无条件、条件谓词或条件相等值构造。 |
| `InspectorCondition` | Truthy/Falsy、Equal/NotEqual、Null/NotNull、Assigned/NotAssigned。 |
| `InspectorMessageType` | Info、Warning、Error。 |

HelpBox 是带状态的提示卡片；Text 是普通说明；Tooltip 不占常驻布局空间。相同语义用于脚本与原生 Inspector。

## 扩展工作流

```csharp
using InnoEngine.Scene;
using InnoEngine.Serialization;
using InnoEditor.Annotations;

public sealed class Mover : GameBehavior
{
    [SerializableProperty]
    [Header("Movement", "World units per second.")]
    [Tooltip("Maximum movement speed.")]
    [Range(0, 30)]
    public float speed { get; set; } = 6;
}
```

新标注派生 `InspectorPresentationAttribute`；对应 EditorScripts 使用 `InspectorAttributeDrawer` 注册，具体接口见 [Inspection](Inno.Editor.Inspection.md)。标注只保存标量/字符串/枚举，不保存运行时服务或对象。没有需重写的 protected 生命周期方法。绘制器由 TypeCache generation 重建，不应静态缓存 collectible 类型。

构造函数拒绝空标题、成员名及无效范围/间距；非法条件会在 Inspector 元数据解析时给出诊断。标注不自动令字段可序列化，仍需显式 `SerializableProperty`。
