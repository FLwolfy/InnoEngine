# Inno.Core.Scripting

[Core 索引](README.md) · [上一页：Assemblies](Inno.Core.Assemblies.md) · [Editor Scripting](../editor/Inno.Editor.Scripting.md) · [Wiki 首页](../README.md)

`Inno.Core.Scripting` 是一个极小、无 Roslyn 依赖的声明程序集。它只定义“哪些真实引擎类型属于脚本 API”的 assembly-level attribute；脚本发现、reference assembly 生成、编译和热重载仍由 Editor 层负责。

## 设计边界

- 每个参与脚本 API 的 feature 项目只保留一个 `Properties/ScriptingApi.cs`。清单通常导出本项目类型，也可以选择性导出依赖项目的类型；不设置反向依赖全部模块的中央清单项目。
- 清单必须逐类型显式导出，不允许用“整个 public assembly 都可用”代替。
- `InnoEngine.Scene`、`InnoEngine.Mathematics` 等名称是稳定且可直接 `using` 的脚本 API namespace；它们映射到一个或多个真实 CLR namespace。
- 导出会为 IDE 生成逻辑 API facade，但 facade 只是编辑期代码模型。Editor 内的热编译仍将逻辑 namespace 转换为真实类型身份，因此热重载、`TypeCacheManager` 和序列化看到的是真实 `Inno.*` 类型体系。
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
ScriptingApiExportAttribute(Type type, string name, ScriptingApiScope scope)
```

将一个 public runtime 类型加入指定 profile。声明 assembly 不需要拥有该类型，因此上层 feature 可以有选择地导出底层依赖的类型；reference builder 始终按 `type.Assembly` 生成 runtime reference，CLR 类型身份不会被错误地改成声明 assembly。该实现 namespace 必须存在唯一的 `ScriptingApiNamespace` 映射。

第二个重载只改变脚本 facade 中的类型名称，最终运行时 IL 仍引用原始 CLR 类型。Editor 会同步处理 IDE reference、普通 `using`、完全限定类型名和 XML documentation identity。例如：

```csharp
[assembly: ScriptingApiExport(
    typeof(ImGuiIcon),
    "AssetIconKind",
    ScriptingApiScope.Editor)]
```

EditorScripts 看到的是 `AssetIconKind`，Host 和运行时仍使用 `ImGuiIcon`。已有 XML 注释会迁移到别名后的 identity；没有 XML 文档的别名常量目录会获得基础 fallback 文档。泛型类型不能更名，因为 C# 不支持为开放泛型声明普通 using alias。

例如 FileBrowser feature 可以在自己的 `ScriptingApi.cs` 中导出 `Inno.Platform.ImGui.ImGuiIcon`，而不需要让底层 ImGui 项目知道 `InnoEditor.Assets`。清单所有权因此属于“决定公开该能力的 feature”，类型的运行时所有权仍属于原程序集。

### ScriptingApiNamespaceAttribute

```csharp
ScriptingApiNamespaceAttribute(
    string name,
    string implementationNamespace,
    ScriptingApiScope scope)
```

把稳定脚本 namespace 映射到真实 namespace。一个脚本 namespace 可以有多个实现 namespace，例如 Scene 同时映射核心类型和 Components：

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

脚本可以直接写：

```csharp
using InnoEngine.Scene;

public sealed class PlayerController : GameBehavior
{
}
```

Editor 会为 IDE 生成一个真正声明 `InnoEngine.Scene.GameBehavior` 等编译期类型的 metadata-only facade。它只保留已导出 API，且生成的 IDE csproj 不参考任何真实引擎 DLL，所以 `Inno.*` 无法被 IDE 解析。

运行时热编译不使用 facade 产物；`ScriptCompiler` 在内存中将已声明的逻辑 `using` 改写到对应实现 namespace，并使用保留真实程序集身份的裁剪参考集编译。最终 IL 因此仍引用 `Inno.Engine.Scene.GameBehavior`，不会把 facade 类型带入运行时。

脚本直接写 `using Inno.Engine.Scene;` 会得到 `INNO2001` 错误；使用已导出类型却没有导入对应逻辑 namespace 会得到 `INNO2002`。这两个诊断同时用于运行时 Roslyn 编译和生成的 IDE 工程。

### Namespace 导入规则

每个脚本文件必须通过普通 `using InnoEngine.*` 或 `using InnoEditor.*` 显式导入自己使用的逻辑 namespace。编译范围导入、隐式导入、MSBuild `Using` item 和 plugin metadata 注入均被禁止。运行时编译器会把每个文件中的逻辑 namespace 映射为该文件可见的实现 namespace，不会把导入扩散到其他源码文件。

## 标准 Properties/ScriptingApi.cs

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

```

未来新增 Render 项目时，只在 `Inno.Rendering/Properties/ScriptingApi.cs` 声明 `InnoEngine.Rendering` 及其导出类型；无需修改 `AssemblyManager`、TypeCache 或中央项目名单。

## 成员可见性

导出单位首先是类型。Editor 在生成裁剪 reference assembly 时只保留 public/protected 签名；若某个成员的签名引用了未导出的非框架类型，该成员不会进入脚本 reference assembly。这避免了通过一个返回内部子系统类型的属性意外扩大 API 闭包。

例如只导出 `StableTypeIdAttribute` 不会顺带让脚本访问 `TypeCacheManager`、`TypeRegistry<TSnapshot>` 或 reload context。

## 常见误区

- Namespace 导入不是访问权限；真正的边界来自显式 export、IDE 逻辑 facade、运行时裁剪 reference assembly 和逻辑 namespace 分析器。
- 不要通过生成源码、项目项或 plugin metadata 注入编译范围导入；每个脚本文件都应显式声明依赖。
- 不要为方便而导出整个 Manager/Registry 程序集。先导出脚本确实需要的最小类型。
- 不要在多个文件分散 assembly attribute。每个项目唯一的 `Properties/ScriptingApi.cs` 是可审查的 API 清单。
- 脚本 API namespace 是稳定的源码契约；真实 CLR namespace 仍决定运行时类型身份。
