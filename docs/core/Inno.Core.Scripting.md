# Inno.Core.Scripting

[Core 索引](README.md) · [上一页：Assemblies](Inno.Core.Assemblies.md) · [Editor Scripting](../editor/Inno.Editor.Scripting.md) · [Wiki 首页](../README.md)

`Inno.Core.Scripting` 是一个极小、无 Roslyn 依赖的声明程序集。它只定义“哪些真实引擎类型属于脚本 API”的 assembly-level attribute；脚本发现、reference assembly 生成、编译和热重载仍由 Editor 层负责。

## 设计边界

- 每个参与脚本 API 的项目只保留一个根级 `ScriptingApi.cs`。
- 清单必须逐类型显式导出，不允许用“整个 public assembly 都可用”代替。
- `InnoEngine.Scene`、`InnoEngine.Mathematics` 等名称是稳定的脚本 API 分组；它们映射到一个或多个真实 CLR namespace。
- 导出不会复制或包装运行时类型。脚本生成的程序集仍引用真实 `Inno.*` 类型标识，热重载、`TypeCacheManager` 和序列化看到的都是同一个类型体系。
- 该项目不知道源文件目录、Roslyn、程序集加载上下文或 Scene 迁移。

## Public API

### ScriptingApiScope

| 值 | 可见范围 |
| --- | --- |
| `Runtime` | `Inno.GameScripts` 和 `Inno.EditorScripts`。 |
| `Editor` | 仅 `Inno.EditorScripts`。 |

### ScriptingApiExportAttribute

```csharp
ScriptingApiExportAttribute(Type type, ScriptingApiScope scope)
```

将一个由当前 assembly 拥有的 public 类型加入指定 profile。类型必须在同一 `ScriptingApi.cs` 中所属的 `ScriptingApiNamespace` 映射内。依赖程序集不能代替它的所有者导出类型。

### ScriptingApiNamespaceAttribute

```csharp
ScriptingApiNamespaceAttribute(
    string name,
    string implementationNamespace,
    ScriptingApiScope scope)
```

把稳定脚本分组映射到真实 namespace。一个脚本分组可以有多个实现 namespace，例如 Scene 同时映射核心类型和 Components：

```csharp
[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene.Components",
    ScriptingApiScope.Runtime)]
```

这里没有生成 `GameObject` 包装器。这样可以避免 facade 对象和真实 Scene 对象产生身份、泛型或序列化不兼容。

### ScriptingGlobalUsingAttribute

```csharp
ScriptingGlobalUsingAttribute(
    string namespaceName,
    ScriptingApiScope scope)
```

请求脚本编译器把一个脚本 API 分组的全部实现 namespace 注入 global usings。参数使用稳定分组名，而不是直接重复 CLR namespace。

## 标准 ScriptingApi.cs

Scene 项目的清单形式如下：

```csharp
using Inno.Core.Scripting;
using Inno.Engine.Scene;
using Inno.Engine.Scene.Components;

[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiNamespace(
    "InnoEngine.Scene",
    "Inno.Engine.Scene.Components",
    ScriptingApiScope.Runtime)]

[assembly: ScriptingApiExport(typeof(GameBehavior), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameComponent), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameObject), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(GameScene), ScriptingApiScope.Runtime)]
[assembly: ScriptingApiExport(typeof(Transform), ScriptingApiScope.Runtime)]

[assembly: ScriptingGlobalUsing(
    "InnoEngine.Scene",
    ScriptingApiScope.Runtime)]
```

未来新增 Render 项目时，只在 `Inno.Rendering/ScriptingApi.cs` 声明 `InnoEngine.Rendering` 及其导出类型；无需修改 `AssemblyManager`、TypeCache 或中央项目名单。

## 成员可见性

导出单位首先是类型。Editor 在生成裁剪 reference assembly 时只保留 public/protected 签名；若某个成员的签名引用了未导出的非框架类型，该成员不会进入脚本 reference assembly。这避免了通过一个返回内部子系统类型的属性意外扩大 API 闭包。

例如只导出 `StableTypeIdAttribute` 不会顺带让脚本访问 `TypeCacheManager`、`TypeRegistry<TSnapshot>` 或 reload context。

## 常见误区

- `ScriptingGlobalUsing` 不是访问权限；真正的边界来自显式 export 和裁剪 reference assembly。
- 不要为方便而导出整个 Manager/Registry 程序集。先导出脚本确实需要的最小类型。
- 不要在多个文件分散 assembly attribute。每个项目唯一的 `ScriptingApi.cs` 是可审查的 API 清单。
- 脚本 API 分组名是稳定的组织契约；真实 CLR namespace 仍决定运行时类型身份。
