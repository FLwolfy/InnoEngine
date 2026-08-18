# Inno.Editor.Scripting

[Editor 索引](README.md) · [Core Scripting](../core/Inno.Core.Scripting.md) · [Assemblies](../core/Inno.Core.Assemblies.md) · [Wiki 首页](../README.md)

`Inno.Editor.Scripting` 管理一个 Project 的 C# 源文件、IDE 工程和脚本程序集代际。它只存在于 Editor；Runtime 通过 `Inno.Core.Assemblies` 接收已编译模块。

## 输入与输出

```text
<Project>/
├─ Assets/**/*.cs                 # GameScripts
├─ Assets/**/*.editor.cs          # EditorScripts
├─ Assets/Plugins/**/*.dll        # Runtime plugin
├─ Assets/Plugins/**/*.editor.dll # Editor-only plugin
├─ Library/ScriptApi/             # 裁剪 reference assemblies
├─ Library/ScriptAssemblies/      # 每代实际脚本 DLL/PDB
├─ Library/IDE/                   # IDE bin/obj
├─ Inno.GameScripts.csproj
├─ Inno.EditorScripts.csproj
└─ InnoProject.sln
```

`Inno.GameScripts` 只能看到 Runtime profile；`Inno.EditorScripts` 同时看到 Runtime、Editor profile，并引用 GameScripts。

## 脚本 API 如何生效

1. `ScriptApiCatalog` 读取各引擎项目唯一的 `ScriptingApi.cs` assembly attributes。
2. 每个 profile 按真实引擎程序集生成同名、同版本的 metadata-only reference assembly。
3. reference assembly 只包含显式导出的类型，以及签名闭包允许的 public/protected 成员。
4. 运行时 Roslyn 编译和生成的 IDE csproj 引用完全相同的 reference assemblies。
5. 脚本 DLL 在运行时按程序集身份绑定真实引擎 DLL，不加载 reference assembly。

这意味着 Rider 不会再因为某个项目被标为 Runtime 而看到该程序集全部 public API。比如导出 `StableTypeIdAttribute` 后，脚本仍无法解析 `TypeCacheManager`。

## ScriptManagerOptions

| 属性 | 默认值 | 说明 |
| --- | --- | --- |
| `required projectRootDirectory` | 无 | 包含 Assets 与 Library 的 Project 根目录。 |
| `autoCompile` | `true` | `Start()` 后是否自动请求首次编译及响应文件变化。 |
| `debounceMilliseconds` | `250` | watcher 的合并延迟。 |

## ScriptManager

| 成员 | 说明 |
| --- | --- |
| `isCompiling` | 当前是否持有编译 gate。 |
| `lastCompilation` | 最近一次完整 Game/Editor 编译结果。 |
| `CompilationCompleted` | 每次完整编译后触发。 |
| `Start()` | 创建目录、生成 IDE 文件、启动 watcher，并按选项请求编译。 |
| `RequestCompile()` | debounce 后异步编译，不在 watcher 线程修改运行时状态。 |
| `CompileAsync(...)` | 编译 GameScripts 和 EditorScripts；任一失败都不产生待激活代际。 |
| `ApplyPendingReload()` | 在主线程安全点加载首次代际，或事务式替换当前代际。 |
| `GenerateProjectFiles()` | 重新生成两个 csproj 和 solution。 |
| `Dispose()` | 停止 watcher、取消任务并卸载活动脚本模块。 |

典型宿主流程：

```csharp
using var scripts = new ScriptManager(new ScriptManagerOptions
{
    projectRootDirectory = projectRoot
});

scripts.Start();

// At the main-thread frame boundary:
scripts.ApplyPendingReload();
```

## 编译结果与诊断

- `ScriptCompilationResult.success`：两个程序集是否全部成功。
- `diagnostics`：`ScriptDiagnostic` 列表。
- `outputDirectory`：本代输出目录。
- `ScriptDiagnostic`：包含 `id`、`severity`、`message`、`filePath`、`line`、`column`。
- `ScriptDiagnosticSeverity`：`Info`、`Warning`、`Error`。

失败时旧代际继续运行，`ApplyPendingReload()` 返回 `false`。

## 热重载边界

成功代际通过 `AssemblyManager.BeginReload()` 准备，Scene 层通过 `SceneReloadService` 捕获并迁移脚本 Component/System。成功后提交并卸载旧 ALC；失败时同时回滚 Scene 结构和程序集 catalog。

无法自动迁移的内容仍包括用户 static 状态、后台线程、第三方事件订阅和外部裸对象引用。脚本应在 `OnDisable()` 释放这些资源。
