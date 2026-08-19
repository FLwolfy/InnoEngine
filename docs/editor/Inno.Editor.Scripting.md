# Inno.Editor.Scripting

[Editor 索引](README.md) · [Core Scripting](../core/Inno.Core.Scripting.md) · [Assets](../assets/README.md) · [Assemblies](../core/Inno.Core.Assemblies.md)

`Inno.Editor.Scripting` 把 C# source、managed plugin 与 assembly definition 当成正式资产，再把它们编译为可回滚的 collectible Script Module。文件发现和变化来源是 Asset Database；该项目没有自己的 `FileSystemWatcher`，也不递归扫描 Project 目录。

## Source 资产

| Source | Asset | Importer | Named outputs |
| --- | --- | --- | --- |
| `*.cs` | `ScriptSourceAsset` | `CSharpScriptImporter` | `source`, `diagnostics`, `asset-state` |
| `*.dll` | `ManagedPluginAsset` | `ManagedPluginImporter` | `assembly`, optional `symbols`/`dependencies`, `asset-state` |
| `*.innoasmdef` | `ScriptAssemblyDefinitionAsset` | `ScriptAssemblyDefinitionImporter` | `source`, `asset-state` |

每个受支持 source 都有 `.imeta` 和 persistent ID。C# 语法错误不会取消 source identity；parse diagnostics 进入 source asset，聚合 assembly build 可以失败并继续运行旧程序集。

Compiler 读取已提交 artifact snapshot，不直接读取正在被外部编辑器写入的 source/plugin 文件。这样一次 build 的 fingerprint、语法树和 plugin bytes 来自同一 Catalog revision。

## Assembly Definition

```json
{
  "name": "Project.Gameplay",
  "scope": "Runtime",
  "references": ["Project.Common"],
  "defines": ["GAMEPLAY_DEBUG"],
  "nullable": true,
  "allowUnsafe": false
}
```

最近父目录的 `.innoasmdef` 决定脚本归属。没有 definition 时：

- `*.editor.cs` 进入 `Inno.EditorScripts`；
- 其他 `.cs` 进入 `Inno.GameScripts`。

显式 asmdef 优先于 filename convention。Editor assembly 可引用 Runtime；Runtime 引用 Editor 会在候选 build 前失败。引用 cycle、未知 assembly、保留名冲突同样作为 compilation diagnostic 返回。

固定 builtin assembly name 不取 Project 文件夹名，因此 Project 改名不会改变 fallback Stable Type ID。

## Project 布局

```text
<Project>/
├─ Assets/
│  ├─ Scripts/**/*.cs
│  ├─ Plugins/**/*.dll
│  └─ **/*.innoasmdef
├─ Library/
│  ├─ AssetDatabase/
│  ├─ Artifacts/
│  │  ├─ ab/cd/<asset-key>/...
│  │  └─ ScriptAssemblies/<build-key>/
│  │     ├─ *.dll
│  │     ├─ *.pdb
│  │     ├─ *.xml
│  │     └─ diagnostics
│  ├─ ScriptApi/
│  └─ IDE/
├─ Inno.GameScripts.csproj
├─ Inno.EditorScripts.csproj
├─ <asmdef-name>.csproj
└─ InnoProject.sln
```

`ScriptAssemblies` 不再出现 `1/2/3...` 数字 generation。目录名是内容 build key：相同输入直接复用，不同输入得到不同 immutable candidate。Editor 启动和成功 reload 后会按 active path、7 天 grace period 与 4 GiB 上限清理不可达 build cache；目录存在只占磁盘，不等于 ALC 常驻内存。

AssemblyManager 自己的 runtime generation 仍存在于 assembly shadow cache，用于区分 collectible ALC；它不写入 `.imeta`、Scene、Prefab 或 artifact identity。

## ScriptManagerOptions

| 属性 | 默认 | 说明 |
| --- | --- | --- |
| `projectRootDirectory` | required | 包含 Assets/Library 的 Project root。 |
| `autoCompile` | `true` | 初始和 Asset change 是否产生 focus-gated compile request。 |
| `debounceMilliseconds` | `250` | request 可消费前的 quiet period。 |

已移除 `retainedCompilationGenerations`。

## ScriptManager

| 成员 | 说明 |
| --- | --- |
| `isCompiling` | compiler gate 是否被占用。 |
| `isCompilationPending` | 是否有等待 focus safe point 的请求。 |
| `compilationProgress` | 真实已完成工作项比例。 |
| `compilationStatus` | 当前 stage。 |
| `lastCompilation` | 最近完整结果。 |
| `CompilationCompleted` | compile attempt 完成事件。 |
| `Start` | 建立 IDE 文件、订阅 `AssetManager.Changed`、请求初始 build。 |
| `RequestCompile` | 标记 dirty，不直接编译。 |
| `TryCompilePending` | quiet period 后消费请求。 |
| `CompileAsync` | 从 Asset snapshot 编译完整 assembly graph。 |
| `ApplyPendingReload` | 主线程安全点首次 Load 或事务 Reload。 |
| `GenerateProjectFiles` | 从 Asset Catalog/asmdef 图生成显式 Compile items。 |
| `Dispose` | 取消任务、取消 Asset observer、卸载活动 Script Module。 |

```csharp
using var scripts = new ScriptManager(new ScriptManagerOptions
{
    projectRootDirectory = projectRoot
});

scripts.Start();

// Consume only after editor focus returns and at a frame boundary.
if (window.isFocused && scripts.TryCompilePending(out Task<ScriptCompilationResult>? task))
{
    ScriptCompilationResult result = await task;
    if (result.success)
        scripts.ApplyPendingReload();
}
```

成功编译不写 Info log；Warning/Error 与源位置保留。失败 candidate 不替换活动 generation。

## 编译进度 modal

Editor 在 focus safe point 开始编译后锁定交互。Modal 使用固定宽度，并始终完成淡入、最短停留和淡出，即使实际编译少于 120 ms。

进度由 project generation、source parse、API analysis、diagnostics、emit 和 reload preparation 等真实工作项推进。Roslyn 单次 Emit 没有内部百分比 callback，因此执行某个工作项时进度会停留，完成后跳到下一个比例；不会用计时器伪造连续进度。

## Scripting API facade

脚本只能引用各项目 `Properties/ScriptingApi.cs` 明确导出的逻辑 API：

```csharp
using InnoEngine.Logging;
using InnoEngine.Reflection;
using InnoEngine.Scene;
using InnoEngine.Serialization;

[StableTypeId("c14f7138-0c5c-4e69-8376-cec8edc3056c")]
public sealed class PlayerController : GameBehavior
{
    [SerializableProperty]
    private float m_speed = 5f;

    protected override void Update()
    {
        Log.Debug("Speed: {0}", m_speed);
    }
}
```

- IDE facade 真正定义 `InnoEngine.*` / `InnoEditor.*` metadata 类型，并携带重写后的 XML documentation。
- IDE csproj 不引用真实 `Inno.*` 实现程序集，因此实现 namespace 无法解析。
- Runtime Roslyn 使用保持真实 CLR identity 的裁剪 reference set，并把逻辑 using 重写为声明的实现 namespace。
- 内建 API 不声明 global using；脚本必须显式 `using InnoEngine.*`。
- `ScriptingGlobalUsing` 能力只保留给明确声明它的第三方 plugin。
- API/export/comment/MVID 变化会改变 facade/build fingerprint。
- `AssetManagerOptions` 等 host initialization 类型不导出；脚本只看到查询、加载、Importer writer 与 Build Processor 所需契约。

`StableTypeId` 不是每个脚本类型的必填项。程序集名与完整类型名不变时 fallback ID 稳定；若要重命名 type/namespace 并迁移已有 Scene 状态，才应显式固定旧 ID。

## IDE 工程

Generator 从 Asset Catalog 与 asmdef graph 生成每个 assembly 的 SDK-style csproj：

- `EnableDefaultItems=false`；
- 只有明确 `Assets/**/*.cs` Compile item；
- `Library` 不显示为 source folder；
- Editor project 引用允许的 Runtime project；
- facade reference 绑定同 fingerprint XML documentation；
- `bin`/`obj`/analyzer/map 全部位于 `Library/IDE`。

IDE 工程是补全和诊断模型；Editor 实际热编译仍使用进程内 Roslyn 与同一 API/catalog 输入。

## Plugin

`Assets/**/Plugins/*.dll` 必须是 managed .NET assembly。`.editor.dll` 只提供给 Editor scope；其他 DLL 可供 Runtime 和 Editor。PDB/deps 是 DLL source unit 的 companion dependency，不单独变成 `BinaryAsset`。

Plugin 的 `ScriptingGlobalUsing` metadata 仍会合并到它可见的 profile。Assembly simple-name 冲突、坏 PE 或缺失依赖会使 candidate build 失败，旧脚本保持活动。

## Reload 与 Scene 状态

成功 build 通过 `AssemblyManager.BeginReload` 准备候选 TypeCache/Registry。`SceneReloadService` 捕获脚本 Component/System 的 Stable Type ID、serialized state、identity、顺序、引用和 lifecycle flags；Activate 后创建新实例并原位替换，全部成功才 Complete。

失败时 Scene 结构和 Assembly transaction 一起 rollback。Edit Mode 不触发生命周期；Runtime reload 会对旧 active instance 调用 `OnDisable`，新 instance 在下一次正常 update 调用 `OnEnable`，不会重复 `Awake`/`Start`，也不会调用 `Reset`/`OnDestroy`。

无法自动迁移 static 字段、后台 Thread/Task、第三方事件订阅或外部裸 CLR 引用。用户代码需要在 `OnDisable` 释放这些资源。
