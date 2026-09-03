# Inno.Editor.Scripting

[Editor 索引](README.md) · [Scripting API](../scripting/Inno.Scripting.Api.md) · [Assets](../assets/README.md) · [Modules](../extensibility/Inno.Extensibility.Modules.md)

`Inno.Editor.Scripting` 把 Project 与已激活 `.iplugin` Mount 中的 C# source、assembly definition 当成正式资产，再把它们编译为可回滚的 collectible Script Module。文件发现和变化来源是统一 Asset Database；该项目没有自己的 `FileSystemWatcher`，也不递归扫描 Project 目录。

## Source 资产

| Source | Asset | Importer | Named outputs |
| --- | --- | --- | --- |
| `*.cs` | `ScriptSourceAsset` | `CSharpScriptImporter` | `source`, `diagnostics`, `type-manifest`, `asset-state` |
| `*.iasmdef` | `ScriptAssemblyDefinitionAsset` | `ScriptAssemblyDefinitionImporter` | `source`, `asset-state` |

两个 Importer 都显式声明 `AssetDeploymentScope.AuthoringOnly`：这些 Asset 在 Editor 中保留 persistent identity、编译输入和诊断，但 Game 导出只部署编译后的 Runtime DLL，不复制 `.cs`、`.iasmdef` 或它们的 source artifact。

每个受支持 source 都有 `.imeta` 和 persistent ID。`type-manifest` 保存该 source 的声明、位置和 partial 信息；聚合编译后还会生成 assembly 级 `*.types.cache`，记录可附加类型最终使用的 source identity、Stable Type ID、类型种类和 canonical source。该文件和 `diagnostics.cache` 都是可丢弃、严格校验的内部编译缓存，不是项目结构化资产。C# 语法错误不会取消 source identity；parse diagnostics 进入 source asset，聚合 assembly build 可以失败并继续运行旧程序集。

Compiler 读取不可变 artifact snapshot，不直接读取正在被外部编辑器写入的 source 文件。普通 Project 编译使用已提交 Asset Catalog；Plugin 安装/更新使用隔离 `AssetSourceMountTransaction` 的候选 Catalog。两者都保证一次 build 的 fingerprint、语法树和 Plugin source 来自同一 revision，但候选 Plugin 在成功前不会出现在 active Asset/Plugin API 中。

每个脚本 assembly 还会生成 `Inno.AssetSource` metadata：Project assembly 的值是 `project`，安装包 assembly 的值是所属 Plugin ID。业务脚本使用 `Assets.LocalPath("Materials/Default.imaterial")` 建立 source-local 引用，因此同一份完整 Project 源码在直接开发时读取 Project Assets，导出并安装为 `.iplugin` 后读取自己的只读 Mount。业务代码不声明、探测或硬编码 Plugin source ID；这项能力属于所有脚本系统，不是 Rendering 专用适配。

Project `Assets` 中的 `~` 目录仍作为普通 source 导入并参与 assembly discovery，开发工程可以直接运行其中代码。相同目录被打入 `.iplugin` 后，在安装态 Mount 中成为待 Import sample，不参与 Plugin assembly；任何 `~` 子树都不会进入 Player runtime closure。

## Assembly Definition

```csharp
var definition = new ScriptAssemblyDefinitionAsset(
    "Project.Gameplay",
    ScriptAssemblyScope.Runtime,
    references: ["Project.Common"],
    defines: ["GAMEPLAY_DEBUG"]);
AssetPipeline.Save("Scripts/Gameplay/Gameplay.iasmdef", definition);
```

`.iasmdef` 是 Inno Serialization 原生资产，不是 JSON 文档。Editor 与工具通过 `AssetPipeline.Save` 和对应 Importer 共用同一序列化、metadata 与依赖通道。

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
│  └─ **/*.iasmdef
├─ Plugins/
│  └─ *.iplugin
├─ Library/
│  ├─ AssetDatabase/
│  ├─ Plugins/<id>/<contentHash>/Assets/
│  ├─ Artifacts/
│  │  ├─ ab/cd/<asset-key>/...
│  │  └─ ScriptAssemblies/
│  │     ├─ .assemblies/<assembly-key>/...
│  │     └─ <generation-key>/
│  │        ├─ *.dll
│  │        ├─ *.pdb
│  │        ├─ *.xml
│  │        ├─ *.types.cache
│  │        └─ diagnostics.cache
│  ├─ ScriptApi/
│  └─ IDE/
├─ Inno.GameScripts.csproj
├─ Inno.EditorScripts.csproj
├─ <asmdef-name>.csproj
└─ InnoProject.sln
```

Project 根目录和 `InnoProject.sln` 只投影用户可编辑的 Project assembly。安装在 `Plugins` 中的代码由同一编译图构建并参与原子 generation，但其 source 与内部 assembly topology 不作为 Rider/IDE 项目暴露；`Inno.GameScripts`、`Inno.EditorScripts` 和 Project `.iasmdef` 工程只引用当前成功 generation 中的 Plugin DLL。每次成功激活都会删除历史遗留的 `Inno.Plugin.*.csproj` 与对应 API map。

`ScriptAssemblies` 不再出现 `1/2/3...` 数字 generation。每个 asmdef/builtin assembly 的 key 覆盖脚本内容 hash、规范化 asmdef 配置、scope/options、公开 Script API contract fingerprint、所属 Source Mount 及直接 dependency key；它不使用无关 Importer 实现 MVID。依赖 key 变化会自然传播到反向依赖，而无关 assembly 直接复用 `.assemblies` 中的 DLL/PDB/XML/type manifest/diagnostics。完整 generation key 组合有序 assembly key 与 Plugin content key，并在成功后一次性形成 load staging。Script assembly cache 使用 7 天 grace period 与 4 GiB 上限；`Library/ScriptApi` reference artifact cache 独立使用 7 天与 512 MiB 上限，并保护当前 Runtime/Editor contract 目录。

增量同时作用于 artifact 与 ALC closure。每个已激活 Plugin 使用独立 `Plugin.<id>` collectible ALC，并通过 manifest 依赖形成显式拓扑；Project Runtime 与 Editor Scripts 各使用一个 ALC。Editor-only 变化只替换 Editor；Runtime 变化替换 Runtime + Editor；某个 Plugin 变化只替换该 Plugin、依赖它的 Plugin 及 Project Scripting。未重编译 assembly 可以复用不可变字节产物，但每个被纳入 closure 的目标 ALC 都是新 generation，下游绑定同一事务中的精确上游 Assembly 实例。

ModuleHost 自己的 runtime generation 仍存在于 assembly shadow cache，用于区分 collectible ALC；它不写入 `.imeta`、Scene、Prefab 或 artifact identity。

## ScriptManagerOptions

| 属性 | 默认 | 说明 |
| --- | --- | --- |
| `projectRootDirectory` | required | 包含 Assets/Library 的 Project root。 |
| `autoCompile` | `true` | 启动与后续 Asset change 是否产生自动编译请求。启动先探测 pending Plugin candidate 与活动 Scripting 状态：只有待激活 `.iplugin` source 才请求 `ReloadPlugins`，否则走可命中内容缓存的 Scripting 请求，不做无条件 Plugin reload。 |
| `debounceMilliseconds` | `250` | 后续 change request 可消费前的 quiet period；首次编译不受影响。 |
| `compilationWarningTimeout` | `10s` | 超过该时长时状态显示 long-running warning；`Timeout.InfiniteTimeSpan` 关闭警告，不会自动取消。 |

已移除 `retainedCompilationGenerations`。

## ScriptManager

| 成员 | 说明 |
| --- | --- |
| `isCompiling` | compiler gate 是否被占用。 |
| `isCompilationPending` | 是否有等待 focus safe point 的请求。 |
| `compilationProgress` | 真实已完成工作项比例。 |
| `compilationStatus` | 当前 stage；超时后包含已运行时长警告。 |
| `compilationElapsed` / `isCompilationTakingLong` | 当前或最近一次耗时，以及是否超过警告阈值。 |
| `lastCompilation` | 最近完整结果。 |
| `Start` | 订阅已提交 Asset/Mount change，并请求 cache-aware 初始 build；不会同步全量 `Rescan`。 |
| `CancelCompilation()` | 取消当前编译等待并保留活动程序集、资产和 GPU generation。 |
| `RecompileScripting()` | 扫描变化、增量编译并只排队必要的反向依赖 ALC closure；无变化不创建 generation。 |
| `ReloadScripting()` | 保留 Plugin generation，强制重建 Runtime + Editor Scripting ALC；有效 artifact 复用。 |
| `ReloadPlugins()` | 重建受影响的 `Plugin.<id>` 模块及依赖 closure；仅在引用 fingerprint 失效时重编脚本 artifact。 |
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

三个 public 操作只排队；内部 scheduler 在 Editor 主线程 focus safe point 捕获已提交 snapshot，后台以串行 Roslyn assembly emit 编译，并仅在候选原子激活的短安全点暂停后续 Module 更新。请求强度为 Recompile < ReloadScripting < ReloadPlugins，并发请求合并为最强项，同时只允许一个 compiler/reload transaction。若新请求在编译期间到达，本次结果会被标记为 superseded 而不发布中间 generation，随后以合并后的最强请求重新取得 source/plugin snapshot。后台编译不调用全局 AssetPipeline，也不冻结 Editor 输入、绘制或普通 Panel 更新。

Assembly reload 使用一组有顺序的 transaction participant，而不是提交后再通知：Play Mode 首先在 candidate 激活前退出并释放瞬态 Runtime Session；TypeRegistry 随后激活候选 snapshot，AssetPipeline 在候选 generation 下完成 Source Catalog 对账；若存在 Plugin source 候选，再临时激活其隔离 Mount/Catalog/Settings；Edit Scene 随后迁移对象，Rendering Runtime 预构造所有活动 Pipeline/Feature。只有全部 participant 与外部 generation 同步成功才提交；后续失败会逆序 Rollback，恢复旧 Mount、Settings、Pipeline/Feature、Scene、TypeCache 和 Asset Catalog。Play simulation 属于一次性运行状态，quiesce 后即使 candidate 回滚也保持 Edit，不从旧 generation 重建。完整提交后才通知 Source Mount 观察者、释放旧 Loader/渲染实例并开始旧 ALC 卸载验证。

Type Registry candidate 在 assembly stage 时可能已经包含 Play Session 等短生命周期 owner。若该 owner 在 participant preparation 中退出，Registry registration 会在 candidate activation 前被撤销；对应 prepared transaction 必须释放 previous/candidate snapshot 并从本次 activation 排除，而不是对已 Dispose 的 registry 调用 `Activate`。这保证“先准备 generation、再 quiesce transient owner”的事务顺序不会产生 `ObjectDisposedException`，也不会让候选 snapshot 反向固定旧 ALC。

这套事务会释放旧脚本 Asset 的 canonical 实例、修复仍存活 host Asset 的引用，并移除已退休脚本代际留在静态事件上的 observer。场景替换任一步失败时会逐项尝试结构、assembly generation、Asset 和旧属性/生命周期补偿；identity observer 在转移期间失败也必须恢复旧对象的注册与附着状态。普通 Project 编译失败不会清除上一个尚未应用的成功 candidate，也不会改变 active generation。唯一不同的是 Plugin availability 已经改变且替代 generation 无法编译：Editor 仍把编译结果报告为 `Failed` 并阻止 Play，但会提交显式 unavailable generation，退休变化 Plugin 与反向依赖脚本 modules，让 Scene 显示可恢复的 Missing，而不是继续运行已从安装集合消失的旧代码。`Dispose` 返回后不会再有活动或排队编译写入状态。

`compilationStatus` 描述 queued、compiling、staged、migrating、committed、unload-verifying、completed 或 failed 阶段。Scripting reload 提交后不会在旧 ALC 仍为 `Pending` 时关闭进度窗口：Editor 在独立后续帧执行有界的 Full GC、finalizer 和第二次 Full GC，并通过只持有弱引用的 monitor 验证退休 context。验证期间进度保持 97%，Editor 仍可操作；新的编译候选消费等待验证完成，避免重叠两个 Assembly transaction。全部 context 不可达后才显示 100% 并关闭窗口。

验证达到十次仍有 context 存活时，modal 显示“reload 已提交但卸载验证失败”，并向 Console 的 `Script Unload` 来源发布 `INNO-ALC-UNLOAD` Error，逐项列出 module、domain/scope 与 generation。这个错误是 post-commit 资源回收失败：新 generation 已经可用且不会伪回滚；外部保存的旧 `Type`、object、delegate、extension、task、subscription 或 thread 仍必须由持有方释放，GC 不能强行破坏可达引用。Host Play Mode 会在 commit 前释放整个 Play Session；Inspector lock 对 Scene identity 只保存 persistent ID，对其他 collectible target 只保存弱引用，因此两类 Editor-owned 长期引用不会固定退休 generation。

## 编译进度 modal

用户从菜单排队请求或文件观察器产生请求时，Editor 立即以 0% 打开阻塞式 Modal，与 Settings Modal 使用相同的后方交互禁用和 popup 规则。Modal 使用固定宽度，状态文字按可用 content width 自动换行，并提供 Cancel。后台编译、Editor frame 与进度绘制继续运行；阻塞的是后方窗口输入，而原子激活候选仍只发生在短 safe point。

进度由 project generation、source parse、API analysis、diagnostics、emit 和 reload preparation 等真实工作项推进。`ScriptCompilationResult.stageTimings` 按执行顺序保留每个完成阶段的 wall time。编译阶段占 0–80%，candidate staging 推进到 86%，Scene/extension 原子迁移推进到 94%，事务提交后进入 97% 的强制卸载验证，只有验证成功、验证确定失败或无需 reload 时才显示 100%。Roslyn 单次 Emit 没有内部百分比 callback，因此执行某个工作项时进度会停留，完成后跳到下一个比例；不会用计时器伪造连续进度。

成功 assembly artifact 同时保存完整的内部 `diagnostics.cache`。启动命中内容缓存时会重新发布同一组 warning，而不是只复用 DLL/PDB 后返回空 diagnostics；因此缓存命中与实际 Roslyn emit 对 Console Panel 具有一致的可观察结果。IDE 投影在 generation 激活之后独立执行，其失败只影响代码编辑体验，并通过 `Script IDE Projection` 诊断来源报告 Warning；它不会把已经成功的脚本候选伪装成编译或激活失败。

## Editor script readiness contract

`IEditorScriptCompilation` 是 host feature 使用的最小只读门禁；internal `EditorScripting` module 实现它，[Play Mode](Inno.Editor.PlayMode.md) 不需要引用 `ScriptManager` 或编译调度实现。

| 成员 | 语义 |
| --- | --- |
| `RequestCompilation()` | 为 Play/Export 等宿主工作流排队一次新的 cache-aware 编译。 |
| `state` | `Initializing`、`Compiling`、`Ready` 或 `Failed`。 |
| `status` | 当前 compiler/reload 阶段的可读说明。 |
| `lastCompilation` | 最近完成的 `ScriptCompilationResult`，首次完成前为 `null`。 |

`Ready` 只表示最近编译成功且 generation 已完成激活；编译、candidate activation 和旧 ALC unload verification 期间均为 `Compiling`。最近失败即为 `Failed`，新的 Play entry 不会静默使用过期脚本。普通编译失败时 active generation 保持不变；Plugin availability 失败时 active generation 已明确退休不可成立的 module closure，场景状态以 Missing 保留。该 contract 是 Editor host API，不加入 EditorScripts 的逻辑 facade。

成功结果的 `runtimeAssemblyPaths` 是本 generation 中所有 runtime-scope main/preload assembly 的去重绝对路径。Game Export 只消费这组路径，因此不会凭输出目录猜测 DLL，也不会把 Editor assembly 部署进 Player；路径对应的 generation 必须在导出开始时仍是最新成功结果。

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
- Script API fingerprint 来自规范化 public/protected contract、逻辑 namespace/type mapping 与可附加类型身份；实现方法体或实现程序集 MVID 单独变化不会使 reference artifact 失效。XML documentation 与该 fingerprint 的不可变 reference artifact 一起生成并复用，不作为独立的二进制公开契约输入。
- `AssetPipelineOptions` 等 host initialization 类型不导出；脚本只看到查询、加载、Importer writer 与 Build Processor 所需契约。

Editor facade 按 feature 分布：

| Facade | 来源 |
| --- | --- |
| `InnoEditor.Core` | `Inno.Editor.Core` lifecycle contracts |
| `InnoEditor.Interactions` | `Inno.Editor.Interactions` Action、Menu、Selection 与 Drag/Drop contracts |
| `InnoEditor.Assets` | `Inno.Editor.Panel.FileBrowser` AssetEditor contracts |
| `InnoEditor.Scene` | `IEditorSceneWorkspace` 查询/工作流接口与 `SceneEdits` 编辑门面 |
| `InnoEditor.Hierarchy` | `Inno.Editor.Panel.Hierarchy` area/action/drop contracts |
| `InnoEditor.Inspection` | `Inno.Editor.Inspection` drawer contracts，以及 `Inno.Editor.Panel.Inspector` 的 area/action/drop contracts |
| `InnoEditor.ImGui` | `ImGuiIcon`、pointer-free `NativeImGui`、`EditorPalette`、`EditorStyleMetrics` 与 widgets |
| `InnoEditor.Settings` | `EditorSetting`、`ProjectSettingEditor<T>` 与对应 placement attributes |

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

Plugin 只能来自项目根 `Plugins/*.iplugin`，并通过 `Plugin.inno`、archive 路径安全、依赖图与 `.imeta` 完整性校验，再进入隔离只读 `AssetSourceMount` 候选；编译与迁移全部成功后才进入统一 active Asset Database。Folder Plugin、`.zip` Plugin、预编译 DLL 和“路径里含 Plugins 就视为插件”的协议不存在。无 `.iasmdef` 时，每个 Plugin ID 自动获得 Runtime 与 Editor 默认程序集；`*.editor.cs` 进入 Editor scope，其余 `.cs` 进入 Runtime scope。显式 `.iasmdef` 必须列在清单 `assemblyDefinitions` 中。

Plugin 只能引用 Host API 与清单声明的依赖 Plugin，不能引用项目脚本；Runtime Scripts 能看到 runtime-scope Plugin，Editor Scripts 可看到 Runtime Scripts 与 Plugin Editor API。每个 Plugin ID 对应独立 `Plugin.<id>` collectible ALC，清单依赖成为 `upstreamModuleNames`，更新/移除只影响反向依赖 closure。错误方向、scope 泄漏、程序集名称冲突、缺失依赖、cycle 或 Roslyn 编译错误会在 stage/publish 前失败；Editor host 会丢弃隔离候选，旧模块 generation、Mount、Catalog、Type/Registry、资产和设置保持活动。ALC 是卸载隔离，不是安全沙箱；把带代码容器放入 `Plugins/` 就表示允许它以项目脚本相同的本机权限执行。

## Reload coordination 与领域状态

成功 build 通过一个多模块 `ModuleHost.BeginReload` 准备完整候选 Assembly catalog、TypeCache 与 Registry。Scripting 随后只把 prepared session 交给 [Inno.Editor.Core](Inno.Editor.Core.md) 的 `EditorReloadCoordinator`；它不知道 Scene、Component/System、Missing 或任何 Panel。Scene 等独立 feature 通过 Core 的中立 participant contract 捕获自己的 generation-bound live state。旧 extension 的 Stop/Detach、交互瞬态清理、候选 Start/Attach 与各 feature 状态恢复都在发布回调返回前完成；全部迁移成功才进入 cleanup-only Complete。

内部 safe-point apply 在成功前只读取 candidate，不提前消费。Plugin/Runtime/Editor 任一 stage、Registry、Asset rescan、Scene restore 或 extension activation 失败都逆序恢复 Scene、extensions、TypeCache、Registry 与全部 previous modules，不发布部分 generation；同一 pending candidate 保留以便显式重试，后续成功编译可原子替换它。

Serialized state 使用逐成员兼容迁移：成员类型保持兼容时恢复旧值；新增成员保留新默认值；删除成员忽略旧数据；同名成员改成不兼容类型时保留新默认值并输出 `INNOHR0001` warning，不会仅因单个字段变化回滚整个程序集。普通 Serialization API 仍保持严格模式。

失败时所有已捕获 feature transaction 和 Assembly transaction 一起反向 rollback。Scene feature 自己实现 Scene migration、Coroutine owner 停止与 Scene diagnostics；Scripting 不调用这些领域 API。Scene 的 Edit Mode migration 不触发生命周期；Runtime reload 会对旧 active instance 调用一次 `OnDisable`，新 instance 在下一次正常 update 调用 `OnEnable`，不会重复 `Awake`/`Start`，也不会调用 `Reset`/`OnDestroy`。

如果候选 generation 缺少 live Component/System，Scene participant 会把对象变为 host-owned missing placeholder，并继续保存 `TypeRef`（落盘仅原逻辑 Stable ID）、原类型名、property bytes、asset dependencies、persistent ID、顺序和图引用。placeholder 身份和 missing 标志不进入 Scene schema，类型消失/恢复本身不改变 clean Scene 的 dirty 状态。类型删除、插件移除、Scene/Prefab round-trip 都不会持有旧 ALC；相同 Stable ID 返回时 `isValid` 自动恢复并原位重建。`INNOHR0002` 完全由 Scene feature 按当前 loaded Scene 与 TypeCache generation 对账：打开带 Missing 的 `.iscene` 后，在 Scene workspace 下一次安全更新就会出现在 Console，不需要等待 Recompile/Reload。每次协调 reload 完成还会请求各领域刷新诊断，因此无变化 Recompile 后仍会重新发布用户 Clear 掉但依然成立的 Missing；类型恢复后该组立即解除。恢复构造、identity 转移、property restore 或失败清理不完整时仍精确回滚到原占位对象。

无法自动迁移 static 字段、后台 Thread/Task、第三方事件订阅或外部裸 CLR 引用。用户代码需要在 `OnDisable` 释放这些资源。
