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
│  │  └─ ScriptAssemblies/
│  │     ├─ .assemblies/<assembly-key>/...
│  │     └─ <generation-key>/
│  │        ├─ *.dll
│  │        ├─ *.pdb
│  │        ├─ *.xml
│  │        ├─ *.types.json
│  │        └─ diagnostics.json
│  ├─ ScriptApi/
│  └─ IDE/
├─ Inno.GameScripts.csproj
├─ Inno.EditorScripts.csproj
├─ <asmdef-name>.csproj
└─ InnoProject.sln
```

`ScriptAssemblies` 不再出现 `1/2/3...` 数字 generation。每个 asmdef/builtin assembly 的 key 分别覆盖自己的 definition、source artifact、scope/options、适用 API、plugin 及直接 dependency key；依赖 key 变化会自然传播到反向依赖，而无关 assembly 直接复用 `.assemblies` 中的 DLL/PDB/XML/type manifest/diagnostics。完整 generation key 只组合有序 assembly key 与 plugin key，并在成功后一次性形成 load staging。Editor 启动和成功 reload 后会对两级 immutable cache 共用 7 天 grace period 与 4 GiB 上限；目录存在只占磁盘，不等于 ALC 常驻内存。

增量同时作用于 artifact 与 ALC closure。物理上下文固定为统一 Plugin ALC、Runtime Scripts ALC、Editor Scripts ALC：Editor-only 变化只替换 Editor；Runtime 变化替换 Runtime + Editor；Plugin 变化替换三者。未重编译 assembly 可以复用不可变字节产物，但每个被纳入 closure 的目标 ALC 都是新 generation，下游绑定同一事务中的精确上游 Assembly 实例，不会出现同名 Plugin/Runtime 类型转换失败。

AssemblyManager 自己的 runtime generation 仍存在于 assembly shadow cache，用于区分 collectible ALC；它不写入 `.imeta`、Scene、Prefab 或 artifact identity。

## ScriptManagerOptions

| 属性 | 默认 | 说明 |
| --- | --- | --- |
| `projectRootDirectory` | required | 包含 Assets/Library 的 Project root。 |
| `autoCompile` | `true` | 启动与后续 Asset change 是否产生自动编译请求。首次请求固定为 `ReloadPlugins` 并立即执行，确保 Plugin、Runtime Scripts 与 Editor Scripts 从统一新 generation 启动；后续请求等待 focus safe point。 |
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
| `Start` | 建立 IDE 文件、订阅 `AssetManager.Changed`、请求初始 build。 |
| `RecompileScripting()` | 扫描变化、增量编译并只排队必要的反向依赖 ALC closure；无变化不创建 generation。 |
| `ReloadScripting()` | 保留 Plugin generation，强制重建 Runtime + Editor Scripting ALC；有效 artifact 复用。 |
| `ReloadPlugins()` | 强制重建统一 Plugin 及两个 Scripting ALC；仅在引用 fingerprint 失效时重编脚本 artifact。 |
| `GenerateProjectFiles` | 从 Asset Catalog/asmdef 图生成显式 Compile items。 |
| `Dispose` | 取消 manager lifetime token，等待活动和排队 compiler gate 工作退出，再取消 Asset observer并按 Editor → Runtime → Plugin 卸载活动模块。 |

```csharp
using var scripts = new ScriptManager(new ScriptManagerOptions
{
    projectRootDirectory = projectRoot
});

scripts.Start();
scripts.RecompileScripting();
scripts.ReloadScripting();
scripts.ReloadPlugins();
```

三个 public 操作只排队；内部 scheduler 在 Editor 主线程 focus safe point 消费请求、后台编译并在安全点激活。请求强度为 Recompile < ReloadScripting < ReloadPlugins，并发请求合并为最强项，同时只允许一个 compiler/reload transaction。若新请求在编译期间到达，本次结果会被标记为 superseded 而不发布中间 generation，随后以合并后的最强请求重新取得 source/plugin snapshot。Asset Update/Rescan 只在主线程内部启动点和 generation 切换安全点发生，后台编译不调用全局 AssetManager。

重载激活后会对账 Asset Database：释放旧脚本 Asset 的 canonical 实例、修复仍存活 host Asset 的引用，并移除已退休脚本代际留在静态事件上的 observer。场景替换任一步失败时会逐项尝试结构、assembly generation、Asset 和旧属性/生命周期补偿；identity observer 在转移期间失败也必须恢复旧对象的注册与附着状态。失败编译不会清除上一个尚未应用的成功 candidate。`Dispose` 返回后不会再有活动或排队编译写入状态。

`compilationStatus` 描述 queued、compiling、staged、migrating、committed、unload-verifying、completed 或 failed 阶段。Scripting reload 提交后不会在旧 ALC 仍为 `Pending` 时关闭 modal：Editor 在独立后续帧执行有界的 Full GC、finalizer 和第二次 Full GC，并通过只持有弱引用的 monitor 验证退休 context。验证期间进度保持 97%，后续 Module 更新和新编译请求消费继续被阻塞。全部 context 不可达后才显示 100% 并关闭 modal。

验证达到十次仍有 context 存活时，modal 显示“reload 已提交但卸载验证失败”，并向 Console 的 `Script Unload` 来源发布 `INNO-ALC-UNLOAD` Error，逐项列出 module、domain/scope 与 generation。这个错误是 post-commit 资源回收失败：新 generation 已经可用且不会伪回滚；外部保存的旧 `Type`、object、delegate、extension、task、subscription 或 thread 仍必须由持有方释放，GC 不能强行破坏可达引用。

## 编译进度 modal

用户从菜单排队请求或文件观察器产生请求时，Editor 立即以 0% 打开 modal；focus safe point 随后消费请求并锁定交互。Modal 使用固定宽度，状态文字按可用 content width 自动换行，并始终完成淡入、最短停留和淡出，即使实际编译很快完成。

进度由 project generation、source parse、API analysis、diagnostics、emit 和 reload preparation 等真实工作项推进。编译阶段占 0–80%，candidate staging 推进到 86%，Scene/extension 原子迁移推进到 94%，事务提交后进入 97% 的强制卸载验证，只有验证成功、验证确定失败或无需 reload 时才显示 100%。Roslyn 单次 Emit 没有内部百分比 callback，因此执行某个工作项时进度会停留，完成后跳到下一个比例；不会用计时器伪造连续进度。百分比文字固定绘制在完整进度条的几何中心，不随已填充区域移动。

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
- Stub builder 会枚举每个 exported type 的全部 public/protected member；签名依赖未导出类型时构建直接失败，并报告完整 member 与缺失类型。只有明确的 host/native API 可以用 member 级 `[ScriptingApiIgnore]` 忽略；Runtime 与 IDE reference assemblies 共用同一闭包校验结果。
- 裁剪 reference 只保留其实现成员同样可见的接口；只由 private 显式成员实现的基础设施接口不会出现在 facade base list，因此不会泄漏 `EditorModule` 的 internal Dispose adapter，也不会产生缺失接口成员的 reference assembly。Module/Panel 项目状态直接由 protected `EditorState` hooks 提供，facade 不导出 Workspace interface、reader/writer 或 JSON DOM。
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
| `InnoEditor.Scene` | `IEditorSceneWorkspace` 查询/工作流接口与 `SceneEdits` 编辑门面 |
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

`Assets/**/Plugins/*.dll` 必须是 managed .NET assembly。`.editor.dll` 由 Plugin asset descriptor 归类为 Editor scope；其他 DLL 为 Runtime scope。所有 Plugin DLL 加入一个统一 collectible generation，原 DLL 不要求写 Inno metadata。PDB/deps 是 DLL source unit 的 companion dependency，不单独变成 `BinaryAsset`。

Plugin 只能引用 InnoInternal 稳定契约，不能引用项目脚本；Runtime Scripts 只能看到 runtime-scope Plugin/Internal，Editor Scripts 可看到 Runtime Scripts、所有 Plugin 和 Internal Editor API。Plugin 类型只能通过脚本文件中的普通 `using` 显式导入。错误方向、scope 泄漏、Assembly simple-name 冲突、坏 PE、缺失依赖或 cycle 会在 stage/publish 前失败，旧三层 generation 保持活动。ALC 是卸载隔离，不是安全沙箱。

## Reload 与 Scene 状态

成功 build 通过一个多模块 `AssemblyManager.BeginReload` 准备完整候选 Assembly catalog、TypeCache 与 Registry。`SceneReloadService` 使用 `TypeRef` 捕获脚本 Component/System 的逻辑类型、serialized state、identity、顺序、引用和 lifecycle flags；长期 payload 只编码 Stable ID。旧 extension 的 Stop/Detach、交互瞬态清理、候选 Start/Attach 与状态恢复都在发布回调返回前完成；全部迁移成功才进入 cleanup-only Complete。

内部 safe-point apply 在成功前只读取 candidate，不提前消费。Plugin/Runtime/Editor 任一 stage、Registry、Asset rescan、Scene restore 或 extension activation 失败都逆序恢复 Scene、extensions、TypeCache、Registry 与全部 previous modules，不发布部分 generation；同一 pending candidate 保留以便显式重试，后续成功编译可原子替换它。

Serialized state 使用逐成员兼容迁移：成员类型保持兼容时恢复旧值；新增成员保留新默认值；删除成员忽略旧数据；同名成员改成不兼容类型时保留新默认值并输出 `INNOHR0001` warning，不会仅因单个字段变化回滚整个程序集。普通 Serialization API 仍保持严格模式。

失败时 Scene 结构和 Assembly transaction 一起 rollback。Edit Mode 不触发生命周期；Runtime reload 会对旧 active instance 调用 `OnDisable`，新 instance 在下一次正常 update 调用 `OnEnable`，不会重复 `Awake`/`Start`，也不会调用 `Reset`/`OnDestroy`。

如果候选 generation 缺少 live Component/System，reload 不再失败：对象会成为 host-owned missing placeholder，并继续保存 `TypeRef`（落盘仅原逻辑 Stable ID）、原类型名、property bytes、asset dependencies、persistent ID、顺序和图引用。placeholder 身份和 missing 标志不进入 Scene schema，类型消失/恢复本身不改变 clean Scene 的 dirty 状态。类型删除、插件移除、Scene/Prefab round-trip 都不会持有旧 ALC；相同 Stable ID 返回时 `isValid` 自动恢复并原位重建。`INNOHR0002` 是独立于 reload event 的当前状态诊断：Editor 主线程协调器在 loaded Scene 实例集合或 TypeCache generation 变化后的下一次安全更新完整替换该诊断组，所以打开一个带 Missing 的 `.iscene` 不需要等待 Recompile/Reload 就会出现在 Console；它只弱跟踪 Scene 实例，不增加 SceneManager 公共事件，也不通过订阅固定旧 ALC。每次 Recompile/Reload 完成还会强制对账，因此无变化 Recompile 不创建 generation，却仍会在 Console 被用户 Clear 后重新发布仍存在的 Missing；类型恢复后该组立即解除。恢复构造、identity 转移、property restore 或失败清理不完整时仍精确回滚到原占位对象。

无法自动迁移 static 字段、后台 Thread/Task、第三方事件订阅或外部裸 CLR 引用。用户代码需要在 `OnDisable` 释放这些资源。
