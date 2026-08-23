# Inno.Editor.Scripting

[Editor 索引](README.md) · [Core Scripting](../core/Inno.Core.Scripting.md) · [Assets](../assets/README.md) · [Assemblies](../core/Inno.Core.Assemblies.md)

`Inno.Editor.Scripting` 把 C# source、managed plugin 与 assembly definition 当成正式资产，再把它们编译为可回滚的 collectible Script Module。文件发现和变化来源是 Asset Database；该项目没有自己的 `FileSystemWatcher`，也不递归扫描 Project 目录。

## Source 资产

| Source | Asset | Importer | Named outputs |
| --- | --- | --- | --- |
| `*.cs` | `ScriptSourceAsset` | `CSharpScriptImporter` | `source`, `diagnostics`, `type-manifest`, `asset-state` |
| `*.dll` | `ManagedPluginAsset` | `ManagedPluginImporter` | `assembly`, optional `symbols`/`dependencies`, `asset-state` |
| `*.iasmdef` | `ScriptAssemblyDefinitionAsset` | `ScriptAssemblyDefinitionImporter` | `source`, `asset-state` |

每个受支持 source 都有 `.imeta` 和 persistent ID。`type-manifest` 保存该 source 的声明、位置和 partial 信息；聚合编译后还会生成 assembly 级 `*.types.json`，记录可附加类型最终使用的 source identity、Stable Type ID、类型种类和 canonical source。C# 语法错误不会取消 source identity；parse diagnostics 进入 source asset，聚合 assembly build 可以失败并继续运行旧程序集。

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

最近父目录的 `.iasmdef` 决定脚本归属。没有 definition 时：

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
│  └─ **/*.iasmdef
├─ Library/
│  ├─ AssetDatabase/
│  ├─ Artifacts/
│  │  ├─ ab/cd/<asset-key>/...
│  │  └─ ScriptAssemblies/<build-key>/
│  │     ├─ *.dll
│  │     ├─ *.pdb
│  │     ├─ *.xml
│  │     ├─ *.types.json
│  │     └─ diagnostics.json
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
| `autoCompile` | `true` | 启动与后续 Asset change 是否产生自动编译请求。首次请求立即执行；后续请求等待 focus safe point。 |
| `debounceMilliseconds` | `250` | 后续 change request 可消费前的 quiet period；首次编译不受影响。 |

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

// The initial request is immediately ready. Later requests wait for a focused frame boundary.
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

进度由 project generation、source parse、API analysis、diagnostics、emit 和 reload preparation 等真实工作项推进。Roslyn 单次 Emit 没有内部百分比 callback，因此执行某个工作项时进度会停留，完成后跳到下一个比例；不会用计时器伪造连续进度。百分比文字固定绘制在完整进度条的几何中心，不随已填充区域移动。

成功 assembly artifact 同时保存完整的 `diagnostics.json`。启动命中内容缓存时会重新发布同一组 warning，而不是只复用 DLL/PDB 后返回空 diagnostics；因此缓存命中与实际 Roslyn emit 对 Console Panel 具有一致的可观察结果。

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
- 所有脚本文件必须显式声明自己使用的 `InnoEngine.*` / `InnoEditor.*` namespace。
- 编译范围导入、MSBuild `Using` item、隐式导入和 plugin metadata 注入均不受支持。
- API/export/comment/MVID 变化会改变 facade/build fingerprint。
- `AssetManagerOptions` 等 host initialization 类型不导出；脚本只看到查询、加载、Importer writer 与 Build Processor 所需契约。

Editor facade 按 feature 分布：

| Facade | 来源 |
| --- | --- |
| `InnoEditor.Core` | `Inno.Editor.Core` lifecycle contracts |
| `InnoEditor.Interactions` | `Inno.Editor.Interactions` Action、Menu、Selection 与 Drag/Drop contracts |
| `InnoEditor.Assets` | `Inno.Editor.Panel.FileBrowser` AssetEditor contracts |
| `InnoEditor.Scene` | `Inno.Editor.Scene` document workspace 与 `SceneEdits` 编辑门面 |
| `InnoEditor.Hierarchy` | `Inno.Editor.Panel.Hierarchy` area/action/drop contracts |
| `InnoEditor.Inspection` | `Inno.Editor.Inspection` drawer contracts，以及 `Inno.Editor.Panel.Inspector` 的 area/action/drop contracts |
| `InnoEditor.ImGui` | `EditorPalette`、`EditorStyleMetrics` 与 widgets |

菜单直接声明在 Action 上，不需要 package class 或集中注册：

```csharp
using InnoEditor.Interactions;

public static class RenderingAreas
{
    public const string MaterialBrowser = "panel/rendering.material-browser";
}

[EditorAction("rendering.create-material", RenderingAreas.MaterialBrowser)]
[EditorMenu(RenderingAreas.MaterialBrowser, "Create/Rendering/Material")]
public sealed class CreateMaterialAction : EditorAction
{
    protected override void Execute(EditorActionContext context) { }
}
```

`StableTypeId` 不是普通脚本 Component/System 的必填项。编译器使用下列 canonical source 规则：

- 每个 `.cs` 的 `.imeta` persistent ID 是 source identity；
- 文件名与 `GameBehavior`/`GameSystem` 类型名匹配的文件是默认 canonical source；`*.editor.cs` 比较时会忽略 `.editor`；
- 单文件只有一个可附加类型时，即使文件名与类型名暂时不一致，也能无歧义继承该 source identity，并给出命名 warning；
- partial 类型只有 canonical 文件提供身份，其他 partial 文件只是 compilation input；
- 一个文件包含多个可附加类型时，只为同名主类型自动分配 source-based ID；其他类型给出 `INNO2001`，应拆文件或显式添加 `[StableTypeId]`；
- 显式 `[StableTypeId]` 始终优先，适合跨 source 移动、特殊多类型布局和手动迁移。

自动 Stable Type ID 由 canonical `.cs.imeta` persistent ID 确定性派生，因此移动文件、文件与类型一起改名、或在唯一 source 内改名都不会改变 Scene 中的类型身份。编译器只生成当前 canonical ID，不注册“程序集名 + 完整类型名”former alias。没有唯一 canonical source 的可附加类型会让编译失败，直到拆分文件或显式添加 `[StableTypeId]`。

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

Plugin 类型只能通过脚本文件中的普通 `using` 显式导入。Assembly simple-name 冲突、坏 PE 或缺失依赖会使 candidate build 失败，旧脚本保持活动。

## Reload 与 Scene 状态

成功 build 通过 `AssemblyManager.BeginReload` 准备候选 TypeCache/Registry。`SceneReloadService` 捕获脚本 Component/System 的 Stable Type ID、serialized state、identity、顺序、引用和 lifecycle flags；Activate 后创建新实例并原位替换，全部成功才 Complete。

Serialized state 使用逐成员兼容迁移：成员类型保持兼容时恢复旧值；新增成员保留新默认值；删除成员忽略旧数据；同名成员改成不兼容类型时保留新默认值并输出 `INNOHR0001` warning，不会仅因单个字段变化回滚整个程序集。普通 Serialization API 仍保持严格模式。

失败时 Scene 结构和 Assembly transaction 一起 rollback。Edit Mode 不触发生命周期；Runtime reload 会对旧 active instance 调用 `OnDisable`，新 instance 在下一次正常 update 调用 `OnEnable`，不会重复 `Awake`/`Start`，也不会调用 `Reset`/`OnDestroy`。

如果候选 generation 缺少 live Component/System 的 replacement，reload diagnostic 会同时包含 retiring CLR type name 与 Stable Type ID，便于将重命名后的类型重新绑定到原身份。

无法自动迁移 static 字段、后台 Thread/Task、第三方事件订阅或外部裸 CLR 引用。用户代码需要在 `OnDisable` 释放这些资源。
