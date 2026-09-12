# InnoEngine 完整项目架构 Overview 与本体收口方案

[架构治理](README.md) · [Identity、可恢复引用与热重载标准](IDENTITY_REFERENCE_RELOAD_STANDARD.md) · [Wiki 首页](../README.md) · [当前问题入口](CURRENT_ISSUES.md)

> 审计快照：2026-09-07。本文是从当前源码推导出的目标架构基线，不是对现有能力的虚构说明。

> 2026-09-08 明确设计约定：Editor UI 扩展允许直接使用 ImGui，包括原生 flags、UI API 与显式 Editor
> scripting exports；不创建替代枚举或包装层隐藏它们。BGFX/SDL3/MiniAudio 后端封装和 ImGui Identity
> DragDrop payload 约束不变。该公开面不是待修复的架构缺口。
> “当前实现”描述已经存在的事实；“目标”与“实施阶段”描述后续工作。历史问题的关闭状态仍只由
> [全量问题台账](../issues/2026-08-31-complete-issue-register.md)维护。

> 本轮逐项实现与验证状态见[统一收口实施记录](ENGINE_CONSOLIDATION_IMPLEMENTATION.md)。
> 本文的目标边界不等于全部项目已经验收，尤其不能把共享 generation gate 等同于所有 Missing 路径已经统一。

当前 C01–C18 实现与验证以 [2026-09-08 实现交付与集中验收](ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md) 为准；本文较早的实施审计段保留为历史对照，不替代最新状态。

## 一、最终结论

InnoEngine 不应追求“只剩几个接口”的极小 MicroKernel。最终产品应是一套完整的默认引擎发行版：

- `Inno.Core.*` 是主要 MicroKernel，提供无游戏世界观的基础能力。
- Assets、Scene、Runtime、Rendering、Audio、Scripting、Build 等是边界明确的引擎能力。
- SDL3、BGFX、MiniAudio、平台 Build Target 和默认 Editor/Player 是 Default Adapters 与 Composition。
- Camera、Sprite、AudioSource、AudioListener、UI、Dialogue、具体 Animation/Physics 模型等属于 Plugin。
- 普通游戏开发者只需要写 Project Scripts 或 Plugin；只有移植平台、替换图形/音频/物理后端的引擎贡献者才需要写 Adapter。

这不是“所有功能都做成 Core”，也不是“所有功能都扔给 Plugin”。正确目标是：

```text
完整默认发行版
├── MicroKernel：可靠、稳定、无游戏世界观
├── Engine Capabilities：以 Content、Services、Runtime 三个视图提供可组合能力
├── Default Adapters：让默认平台开箱即用
└── Bundled Plugins：提供常用模型，但仍可删除、替换和热重载
```

当前架构基础不需要推倒重写。Module/Type generation、Serialization、Asset Pipeline、Scene 隔离、
Rendering graph、Plugin mount、Build staging 和 Editor extension 已经形成了正确主干。本轮已经建立统一
Runtime Subsystem Pipeline、可撤销 execution scope、统一 DiagnosticReporter、Animation target/binding、
真实异步 Audio preparation、Identity 内容 scope、Reference Catalog 和 GC unload barrier。
默认装配由本地声明生成强类型调用，Solution 与 Architecture Tool 同时检查物理目录及概念边界。
尚未完整关闭的是跨域 Missing/recovery 生产路径统一、pending 生命周期资源的安全退出、全部发布快照冻结、
领域内部职责拆分与所有队列预算。验收状态必须逐项查看实施记录，不因主路径可运行而省略这些缺口。

## 二、审计范围与依据

本次审计针对架构而不是单一功能，检查了：

- 全仓 `.csproj` 引用图、Player deployment closure、Solution 分类与 Architecture Tool。
- `EngineHost`、`RuntimeSession`、Player 和 Editor composition root 的创建、帧循环与逆序释放。
- ModuleHost、TypeCatalog、TypeRegistry、Scripting reload 和 Plugin activation 的候选、激活、回滚、退休流程。
- Authoring `AssetPipeline` 与 Player `AssetDatabase` 的加载、Artifact、generation 和资源所有权。
- SceneWorld、GameBehavior、GameSystem、Scene/Prefab 序列化和 Play Mode 隔离。
- Rendering contract/runtime/BGFX/Scene bridge 与 Audio contract/runtime/MiniAudio。
- Build snapshot、Support Pack、平台 target、Plugin package 与 Player 启动流程。
- Editor extension、History、状态恢复、Play Mode、Rendering 和 Audio host。
- 代码体积最大的职责中心及事件、日志、Job、native callback 的线程边界。

关键源码入口：

- [EngineHost](../../src/runtime/engine/Inno.Runtime/Hosting/EngineHost.cs)
- [RuntimeSession](../../src/runtime/engine/Inno.Runtime/Hosting/RuntimeSession.cs)
- [Player composition root](../../src/composition/player/Inno.Player/GamePlayerHost.cs)
- [Editor composition root](../../src/composition/editor/host/Inno.Editor.Application/Hosting/EditorHost.cs)
- [ModuleHost](../../src/foundation/extensibility/Inno.Extensibility.Modules/ModuleHost.cs)
- [TypeRegistry](../../src/foundation/extensibility/Inno.Extensibility.Types/TypeRegistry.cs)
- [AssetPipeline](../../src/content/assets/Inno.Assets.Pipeline/AssetPipeline.cs)
- [AssetDatabase](../../src/content/assets/Inno.Assets/Runtime/AssetDatabase.cs)
- [LayerStack](../../src/foundation/core/Inno.Core.Layers/LayerStack.cs)
- [RenderRuntime](../../src/services/rendering/Inno.Rendering.Runtime/RenderRuntime.cs)
- [AudioRuntime](../../src/services/audio/Inno.Audio.Runtime/AudioRuntime.cs)
- [PluginEnvironment](../../src/runtime/plugins/Inno.Plugins.Authoring/PluginEnvironment.cs)
- [GameBuildPipeline](../../build/pipeline/Inno.Build/Game/GameBuildPipeline.cs)
- [ArchitectureRules](../../tools/Inno.Tooling.Architecture/ArchitectureRules.cs)

本文不把“大文件”自动判为坏设计。只有当一个文件同时承担多个可独立变化的所有权、事务或生命周期时，
才把它列为拆分候选。

## 三、四层最终模型

### 3.1 Layer 1：MicroKernel

`Inno.Core.*` 是主要 MicroKernel。它只允许包含下列性质的能力：

- 不依赖具体游戏世界观。
- 不依赖 Scene、Rendering、Audio、Editor、Build 或 Native backend。
- 可被两个以上无关机制复用。
- 生命周期和线程语义可以独立描述。
- 没有平台设备、窗口、GPU、声卡或业务资源的所有权。

当前适合保留在 Core 的内容：

| Core 家族 | 稳定职责 |
| --- | --- |
| Mathematics | 数学值类型和算法 |
| Identity | runtime/persistent identity 与 generation-safe identity |
| Serialization | 当前格式、中立节点、converter generation 与 operation context |
| Storage / Graphs | 通用存储、依赖图和图文档机制 |
| Events | backend-neutral event 与有序分发 |
| Layers | product-neutral Layer/Overlay 顺序、帧 callback 与 EventHub scope |
| Jobs / Coroutines | Session 可拥有的调度原语 |
| IO | 原子文件/目录提交与路径边界 |
| Diagnostics / Logging | 实例化诊断状态与日志路由 |
| Settings | 中立设置协议、贡献与实例 store |
| Input | 物理键、按钮、光标等最小值语义 |

`Inno.Extensibility.*` 和 `Inno.Scripting.Api` 位于 MicroKernel 之上的最低机制层。它们虽然不是
`Inno.Core.*`，但仍是所有可热重载上层系统共用的基础设施。当前 Core Logging 使用 Assembly
Domain/Scope 元数据，因此这三者应视为同一个“基础闭包”，不能让 Assets、Rendering、Audio 或
Editor 反向进入该闭包。

以下内容永远不进入 Core：

- Camera、Sprite、Mesh、Light、AudioSource、AudioListener。
- Animation Controller、Timeline、Physics Body、Collider、UI Widget。
- SDL、BGFX、MiniAudio、Jolt 等 backend 类型。
- Editor Panel、Importer、Build Target 或 Player bootstrap。
- 为单一 Plugin 服务的枚举、配置和便利封装。

### 3.2 Layer 2：Engine Capabilities

这一层提供稳定、后端中立、可组合的引擎能力，但不应内建某一种游戏表现模型。为避免所有项目都挤在
一个笼统的 `mechanisms` 节点下，Solution 将同一依赖层按职责拆成三个、且只拆成三个平级视图：

- `content`：Identity 之上的项目内容、引用、资产、Scene 与中立 Animation 数据。
- `services`：Platform、Input、Storage、Rendering 与 Audio 等宿主服务。
- `runtime`：统一 Session pipeline、Scripting 与 Plugin generation orchestration。

三者是同一个概念依赖层的职责视图，不表示 `content → services → runtime` 的强制线性依赖；它们都只能
向 Foundation 或同层稳定契约依赖，并共同被 Adapter 与 Composition 装配。

| 引擎能力家族 | 应拥有的内容 | 不应拥有的内容 |
| --- | --- | --- |
| Extensibility | Module generation、Type catalog、registry transaction | 具体 Rendering/Audio 类型名单 |
| Scripting | 显式 API export、compile、reload、ALC 生命周期 | 中央业务 API 白名单 |
| Assets | Asset identity、lookup、Artifact contract、authoring pipeline | Rendering/Audio 专用 runtime 策略 |
| Scene | World、Object、Component、System、Transform、生命周期和序列化 | Camera、Renderer、AudioSource、PhysicsBody |
| Runtime | Host、Session、clock、frame pipeline、subsystem lifetime | BGFX/MiniAudio/SDL 或具体玩法模型 |
| Rendering | device/resource/command/graph/pipeline/provider contract 与 runtime | 2D、3D、PBR、Camera、Light |
| Audio | device/clip/voice/bus/mixer/spatial/provider contract 与 runtime | Scene AudioSource/Listener、Dialogue、Music system |
| Platform | application/window/native-handle contract | SDL enum 或 SDL pointer |
| Build | build request、target contract、snapshot、staging、Support Pack contract | Editor UI 与目标硬编码名单 |
| Animation | clip/track/sampling/blending/event 的中立协议 | Sprite、Skeleton、Transform 或 Cutscene 世界观 |
| Runtime Storage | 沙箱化持久数据与原子存储契约 | 游戏 Save schema、slot UI 或 cloud provider |

引擎能力可以被默认发行版自动安装，因此“不是 Core”不等于“用户必须手动选择”。例如 Audio
是标准 Service，MiniAudio 是默认 Adapter implementation；用户无需为每个项目配置后端。

### 3.3 Layer 3：Default Adapters 与 Composition

Adapter 把中立引擎能力连接到 OS、native library、设备或目标平台：

| 当前默认 Adapter/Host | 责任 |
| --- | --- |
| `Inno.Adapter.Platform.Sdl3` | SDL3 application/window/event 到 Platform contract |
| `Inno.Adapter.Rendering.Bgfx` | BGFX 到 `IRenderDevice` |
| `Inno.Adapter.Audio.MiniAudio` | MiniAudio 到 `IAudioDevice` |
| `Inno.Adapter.Presentation` | Host presentation 的中立契约 |
| `Inno.Adapter.Presentation.ImGui.Sdl3` / `Inno.Adapter.Presentation.ImGui.Bgfx` | 默认 Editor presentation implementation |
| `Inno.Adapter.Default` | Player 使用的标准 runtime adapter catalog |
| `Inno.Adapter.Authoring.Default` | Editor 额外使用的 authoring/presentation catalog |
| `Inno.Shell` | Player 与 Editor 共用的 backend-neutral composition lifecycle |
| `Inno.Build.Platform.*` | 目标布局与目标内容编译 |
| `Inno.Player` | 默认 Player composition root |
| `Inno.Editor.Application` | 默认 Editor composition root |
| Toolchains / Native | 构建或绑定原生依赖，不进入游戏脚本 API |

默认 Host 通过 `AdapterSelection` 的开放 `RenderingBackendId` 选择渲染实现；`PlatformBackend`、
`AudioBackend` 等其他 family 继续使用各自中立选择协议。Host 通过 `IAdapterCatalog` /
`IAuthoringAdapterCatalog` 获得中立对象。SDL3、BGFX、MiniAudio、
FileSystem 与 ImGui bridge 的类型创建全部收口在默认 catalog 内，`GamePlayerHost`、`EditorHost` 和
`Inno.Shell` 均不得引用或公开这些具体类型。Host 同样不得知道 `AudioSource`、`Camera2D`、
`SpriteRenderer2D` 等 Feature Model，也不得使用 `typeof(T)` 作为隐式注册机制。

替换 backend 是引擎移植工作，不是普通 gameplay Plugin 工作。Adapter：

- 在应用启动时选择。
- 默认不参与脚本热重载。
- 可以引用 Native binding。
- 必须只实现中立 Device/Platform contract。
- 必须由 Support Pack 和 Build Target 提供匹配的目标产物。

`Inno.Shell` 是 Composition 中唯一通用 Host 基类。它只拥有 Player 与 Editor 必然共享的应用级生命周期：

- 通过 `IAdapterCatalog` 和 `AdapterSelection` 创建中立 application、primary window、input event source 与 render device。
- 统一执行事件泵、input 路由、主窗口 resize、frame timing、退出判定和逆序释放。
- 通过 `OnStarting`、`OnEvent`、`OnFrame`、`OnStopping` 与 `DisposeProductResources` 提供产品 hook。
- `GamePlayerHost : Shell` 负责 game RuntimeSession；`EditorHost : Shell` 负责 authoring、Edit/Play session 与 presentation。

Shell 不是旧式全局 Service Locator，也不拥有所有领域对象。Audio、Storage 和 Runtime Subsystem 仍按 Session
由派生产品通过中立 catalog 创建；Editor 专属 shader compiler 与 presentation 只通过
`IAuthoringAdapterCatalog` 获得。`Inno.Adapter.Default` 不依赖 Build/Authoring；
`Inno.Adapter.Authoring.Default` 才增加 BGFX tools 与 ImGui presentation，因此 Player 发布闭包不会带入
Compiler、AssetPipeline、Toolchain 或 Editor assembly。

`Layer`/`LayerStack`、`Shell` 与 Runtime Subsystem 是三种不同尺度的生命周期。Layer 是 Core 中可复用的局部
顺序原语；Shell 是 Composition 中的 application Host；Runtime Subsystem 是 RuntimeSession 的正式能力管线。
Shell 不强制拥有一个全局 LayerStack，Player 可以只使用 Runtime Subsystem，Editor 则可以在 Shell frame 内用
LayerStack 排列 presentation layer。这样既保留通用 Layer API，也不会把产品装配重新塞回 Core。

### 3.4 Layer 4：Plugins

Plugin 组合中立引擎能力形成游戏世界观和高级功能。以下内容应当是 Plugin，即使它由官方维护并默认附带：

| Plugin 类别 | 示例 |
| --- | --- |
| Rendering model | 2D、3D、PBR、Forward、Deferred、Camera、Sprite、Mesh、Light、Particle |
| Audio model | AudioSource、AudioListener、BGM、Dialogue、ducking、reverb、occlusion |
| Animation model | Sprite animation、skeletal animation、property binding、Animator Controller、Timeline |
| Gameplay/UI | UI、Visual Novel、Dialogue、Localization、Save slot、Input Actions |
| Simulation | Physics 2D/3D Scene components、navigation、AI、terrain |
| Connectivity | networking、VOIP、cloud save、telemetry provider |

“Bundled Plugin”只是发布方式，不改变架构归属。它仍必须：

- 只依赖脚本导出的中立契约。
- 用 Attribute + TypeCatalog 自动发现。
- 用 Stable ID 与中立序列化 bytes 保存状态。
- 支持缺席、失败隔离、卸载和 generation 替换。
- 不要求修改 Runtime、Player 或 Editor 的中央 switch。

### 3.5 三种架构视图

本仓库同时存在三种用途不同、不能互相替代的结构视图：

| 视图 | 回答的问题 | 约束来源 |
| --- | --- | --- |
| 磁盘目录 | 项目在依赖模型中的角色和二级领域 | `foundation → {content, services, runtime} → adapters → composition` |
| Solution Folder | 在 IDE 中如何呈现同一架构 | 必须与磁盘目录逐级一致 |
| Distribution | 最终 Player/Editor 交付包含什么 | Build Target 与 Support Pack manifest |

`InnoEngine Distribution` 仍然不是源码目录树，它只描述发布闭包。源码磁盘目录和 Solution Explorer
则共同展示同一套“概念层优先、领域二级归类”结构。任何新项目必须同时进入唯一物理目录和同路径
Solution Folder；二者不允许出现不同分类。

### 3.6 最终磁盘目录

```text
src/
├── foundation/
│   ├── core/
│   ├── extensibility/
│   └── scripting/
├── content/
│   ├── references/
│   ├── assets/
│   ├── scene/
│   └── animation/
├── services/
│   ├── platform/
│   ├── input/
│   ├── storage/
│   ├── rendering/
│   └── audio/
├── runtime/
│   ├── contracts/
│   ├── engine/
│   ├── generators/
│   ├── scripting/
│   └── plugins/
├── adapters/
│   ├── common/
│   ├── default/
│   ├── platform/
│   ├── input/
│   ├── storage/
│   ├── rendering/
│   ├── audio/
│   └── presentation/
└── composition/
    ├── default/
    ├── shell/
    ├── player/
    └── editor/
        ├── host/
        ├── framework/
        ├── features/
        ├── presentation/
        └── panels/

native/                   # C# raw ABI binding projects
extern/                   # pinned upstream source submodules
build/
├── pipeline/
├── support/
└── toolchains/
tools/                    # repository architecture and development tooling
tests/
├── <domain>/             # one real directory per test Solution domain
└── <domain>/fixtures/    # reload/test support assemblies
docs/                     # Wiki and mandatory architecture standards
```

每个项目目录必须采用 `<概念层>/<领域>/<ProjectName>`。例如 Audio 的中立能力位于
`src/services/audio`，默认实现位于 `src/adapters/audio`；这种距离是有意的，它让“稳定机制”和
“可替换实现”在操作系统目录上也不会混淆。`tests` 同样使用真实领域目录，fixture 使用真实
`fixtures` 子目录。`native`、`build`、`tools` 已经按其顶层角色组织，不伪装成 `src/adapters`。

### 3.7 最终 Solution Folder

```text
InnoEngine
├── src
│   ├── foundation
│   │   ├── core                 # Inno.Core.*
│   │   ├── extensibility        # Inno.Extensibility.*
│   │   └── scripting            # Inno.Scripting.Api
│   ├── content
│   │   ├── references           # Inno.References
│   │   ├── assets               # Inno.Assets, Inno.Assets.Pipeline
│   │   ├── scene                # Inno.Scene, Inno.Scene.Assets
│   │   └── animation            # Inno.Animation, Assets, Runtime
│   ├── services
│   │   ├── platform             # Inno.Platform
│   │   ├── input                # Inno.Input, Inno.Input.Runtime
│   │   ├── storage              # Inno.Storage, Inno.Storage.Runtime
│   │   ├── rendering            # neutral Rendering, Runtime, Assets, Shaders
│   │   └── audio                # neutral Audio, Runtime, Assets
│   ├── runtime
│   │   ├── contracts            # Inno.Runtime.Contracts
│   │   ├── engine               # Inno.Runtime
│   │   ├── generators           # Inno.Runtime.Generators, build-time only
│   │   ├── scripting            # Compiler and Reload
│   │   └── plugins              # Plugin runtime and authoring orchestration
│   ├── adapters
│   │   ├── common               # IAdapterCatalog and runtime backend selection
│   │   ├── default              # runtime and authoring default catalogs
│   │   ├── platform             # neutral platform adapter contract + SDL3 implementation
│   │   ├── input                # neutral input adapter contract + SDL3 implementation
│   │   ├── storage              # neutral storage adapter contract + FileSystem implementation
│   │   ├── rendering            # neutral rendering adapter contract + BGFX implementation
│   │   ├── audio                # neutral audio adapter contract + MiniAudio implementation
│   │   └── presentation         # neutral presentation contract + ImGui SDL3/BGFX implementation
│   └── composition
│       ├── default              # Inno.Engine.Default, typed subsystem composition
│       ├── shell                # Inno.Shell
│       ├── player               # Inno.Player : Shell
│       └── editor
│           ├── host             # Inno.Editor.Application
│           ├── framework        # shared Editor runtime/contracts
│           ├── features         # domain-facing Editor integration
│           ├── presentation     # Inno.Editor.ImGui
│           └── panels           # Inno.Editor.Panel.*
├── native                       # Inno.Native.* raw ABI projects
├── build
│   ├── pipeline
│   ├── support
│   └── toolchains
├── tools
└── tests                        # assets/audio/... domain folders and fixtures
```

每个 `.csproj` 在 Solution 中只能有一个归属，而且该 Solution Folder 必须等于项目目录去掉最后一级
`<ProjectName>` 后的路径。测试按领域分类，fixture 必须同时进入真实和 Solution 的 `fixtures`。
`extern`、`.lib` 与 `docs` 不加入 Solution，因为它们不是可构建项目。

### 3.8 当前项目的唯一 Solution 归属

以下清单是当前 `.sln` 和物理目录共同使用的 canonical assignment。新增、删除或重命名项目时，必须同时
更新磁盘目录、`.sln`、Architecture Tool 分类和对应 Wiki。

| Solution Folder | 项目 |
| --- | --- |
| `src/foundation/core` | `Inno.Core.Coroutines`、`Inno.Core.Diagnostics`、`Inno.Core.Events`、`Inno.Core.Execution`、`Inno.Core.Graphs`、`Inno.Core.Identity`、`Inno.Core.Input`、`Inno.Core.IO`、`Inno.Core.Jobs`、`Inno.Core.Layers`、`Inno.Core.Logging`、`Inno.Core.Mathematics`、`Inno.Core.Serialization`、`Inno.Core.Serialization.Generators`、`Inno.Core.Settings`、`Inno.Core.Collections` |
| `src/foundation/extensibility` | `Inno.Extensibility.Modules`、`Inno.Extensibility.Types`、`Inno.Extensibility.Reload` |
| `src/foundation/scripting` | `Inno.Scripting.Api` |
| `src/content/references` | `Inno.References` |
| `src/content/assets` | `Inno.Assets`、`Inno.Assets.Pipeline` |
| `src/content/scene` | `Inno.Scene`、`Inno.Scene.Assets` |
| `src/content/animation` | `Inno.Animation`、`Inno.Animation.Assets`、`Inno.Animation.Runtime` |
| `src/services/platform` | `Inno.Platform` |
| `src/services/input` | `Inno.Input`、`Inno.Input.Runtime` |
| `src/services/storage` | `Inno.Storage`、`Inno.Storage.Runtime` |
| `src/services/rendering` | `Inno.Rendering`、`Inno.Rendering.Runtime`、`Inno.Rendering.Assets`、`Inno.Rendering.Shaders` |
| `src/services/audio` | `Inno.Audio`、`Inno.Audio.Runtime`、`Inno.Audio.Assets` |
| `src/runtime/engine` | `Inno.Runtime` |
| `src/runtime/contracts` | `Inno.Runtime.Contracts` |
| `src/runtime/generators` | `Inno.Runtime.Generators` |
| `src/runtime/scripting` | `Inno.Scripting.Compiler`、`Inno.Scripting.Reload` |
| `src/runtime/plugins` | `Inno.Plugins`、`Inno.Plugins.Authoring` |
| `src/adapters/common` | `Inno.Adapter` |
| `src/adapters/default` | `Inno.Adapter.Default`、`Inno.Adapter.Authoring.Default` |
| `src/adapters/platform` | `Inno.Adapter.Platform`、`Inno.Adapter.Platform.Sdl3` |
| `src/adapters/input` | `Inno.Adapter.Input`、`Inno.Adapter.Input.Sdl3` |
| `src/adapters/storage` | `Inno.Adapter.Storage`、`Inno.Adapter.Storage.FileSystem` |
| `src/adapters/rendering` | `Inno.Adapter.Rendering`、`Inno.Adapter.Rendering.Authoring`、`Inno.Adapter.Rendering.Bgfx` |
| `src/adapters/audio` | `Inno.Adapter.Audio`、`Inno.Adapter.Audio.MiniAudio` |
| `src/adapters/presentation` | `Inno.Adapter.Presentation`、`Inno.Adapter.Presentation.ImGui.Sdl3`、`Inno.Adapter.Presentation.ImGui.Bgfx` |
| `src/composition/shell` | `Inno.Shell` |
| `src/composition/default` | `Inno.Engine.Default` |
| `src/composition/player` | `Inno.Player` |
| `src/composition/editor/host` | `Inno.Editor.Application` |
| `src/composition/editor/framework` | `Inno.Editor.Core`、`Inno.Editor.Diagnostics`、`Inno.Editor.Graph`、`Inno.Editor.Inspection`、`Inno.Editor.Interactions`、`Inno.Editor.Settings` |
| `src/composition/editor/features` | `Inno.Editor.Audio`、`Inno.Editor.Exporting`、`Inno.Editor.PlayMode`、`Inno.Editor.Rendering`、`Inno.Editor.Scene`、`Inno.Editor.Scripting` |
| `src/composition/editor/presentation` | `Inno.Editor.ImGui` |
| `src/composition/editor/panels` | `Inno.Editor.Panel.FileBrowser`、`Inno.Editor.Panel.GameView`、`Inno.Editor.Panel.Global`、`Inno.Editor.Panel.Hierarchy`、`Inno.Editor.Panel.Inspector`、`Inno.Editor.Panel.Logging`、`Inno.Editor.Panel.SceneView`、`Inno.Editor.Panel.Settings`、`Inno.Editor.Panel.ShaderEditor`、`Inno.Editor.Panel.Stats` |

其余可构建项目保持独立顶层角色：

| Solution Folder | 项目 |
| --- | --- |
| `native` | `Inno.Native.LibraryLoading`、`Inno.Native.Sdl3`、`Inno.Native.Bgfx`、`Inno.Native.Bgfx.Tools`、`Inno.Native.MiniAudio`、`Inno.Native.ImGui`、`Inno.Native.ImGuizmo` |
| `build/pipeline` | `Inno.Build`、`Inno.Build.Cli`、`Inno.Build.Platform.MacOS`、`Inno.Build.Platform.Windows` |
| `build/support` | `Inno.Build.SupportPacks` |
| `build/toolchains` | `Inno.Build.Toolchains`、`Inno.Build.Toolchains.Sdl3`、`Inno.Build.Toolchains.Bgfx`、`Inno.Build.Toolchains.Bgfx.Tools`、`Inno.Build.Toolchains.ImGui`、`Inno.Build.Toolchains.ImGuizmo`、`Inno.Build.Toolchains.MiniAudio` |
| `tools` | `Inno.Tooling.Architecture` |

测试项目完整按领域归属：

| Solution Folder | 项目 |
| --- | --- |
| `tests/assets` | `Inno.Assets.Tests`、`Inno.Assets.Pipeline.Tests` |
| `tests/audio` | `Inno.Audio.Tests`、`Inno.Audio.Assets.Tests`、`Inno.Audio.Runtime.Tests`、`Inno.Adapter.Audio.MiniAudio.Tests` |
| `tests/animation` | `Inno.Animation.Tests` |
| `tests/runtime` | `Inno.Runtime.Tests`、`Inno.Runtime.Generators.Tests` |
| `tests/build` | `Inno.Build.Tests` |
| `tests/core` | `Inno.Core.Coroutines.Tests`、`Inno.Core.Execution.Tests`、`Inno.Core.Diagnostics.Tests`、`Inno.Core.Events.Tests`、`Inno.Core.Graphs.Tests`、`Inno.Core.IO.Tests`、`Inno.Core.Identity.Tests`、`Inno.Core.Jobs.Tests`、`Inno.Core.Layers.Tests`、`Inno.Core.Logging.Tests`、`Inno.Core.Mathematics.Tests`、`Inno.Core.Serialization.Tests`、`Inno.Core.Collections.Tests` |
| `tests/editor` | `Inno.Editor.Audio.Tests`、`Inno.Editor.Graph.Tests`、`Inno.Editor.Interactions.Tests`、`Inno.Editor.PlayMode.Tests` |
| `tests/extensibility` | `Inno.Extensibility.Modules.Tests`、`Inno.Extensibility.Types.Tests`、`Inno.Extensibility.Reload.Tests` |
| `tests/extensibility/fixtures` | `Inno.Extensibility.Modules.TestDependency`、`Inno.Extensibility.Modules.TestModule.V1`、`Inno.Extensibility.Modules.TestModule.V2`、`Inno.Extensibility.Modules.TestModule.Invalid`、`Inno.Extensibility.Types.TestAssemblyA`、`Inno.Extensibility.Types.TestAssemblyB` |
| `tests/input` | `Inno.Input.Tests` |
| `tests/native` | `Inno.Native.Sdl3.Tests`、`Inno.Native.Bgfx.Tests`、`Inno.Native.Bgfx.Tools.Tests`、`Inno.Native.MiniAudio.Tests`、`Inno.Native.ImGui.Tests`、`Inno.Native.ImGuizmo.Tests` |
| `tests/player` | `Inno.Player.E2E` |
| `tests/plugins` | `Inno.Plugins.Tests` |
| `tests/references` | `Inno.References.Tests` |
| `tests/rendering` | `Inno.Rendering.Tests`、`Inno.Rendering.Assets.Tests`、`Inno.Rendering.Shaders.Tests`、`Inno.Rendering.Runtime.Tests`、`Inno.Adapter.Rendering.Bgfx.Tests` |
| `tests/rendering/fixtures` | `Inno.Rendering.Runtime.Reload.TestModule` |
| `tests/scene` | `Inno.Scene.Tests` |
| `tests/scene/fixtures` | `Inno.Scene.Reload.TestModule` |
| `tests/scripting` | `Inno.Editor.Scripting.Tests` |
| `tests/storage` | `Inno.Storage.Tests` |

`Inno.Audio.Scene`、`Inno.Audio.Scene.Tests` 已删除，不得重新加入任何分类。

### 3.9 Native、Adapter、Toolchain 与 Support Pack

Native binding 只描述 ABI，Adapter 才把 ABI 翻译为稳定引擎契约：

```text
extern/<library>
    ├── build/toolchains/Inno.Build.Toolchains.<Library>
    │       └── .lib/<library>/<rid>/<configuration>
    ├── native/Inno.Native.<Library>
    └── src/adapters/<domain>/Inno.Adapter.<Domain>.<Backend>
            └── build/support/Inno.Build.SupportPacks
                    └── Player native/ release closure
```

- `extern` 是固定 commit 的上游源码，不是运行时依赖。
- `.lib` 是可完全重建的开发产物，不进入源码模型。
- `Inno.Native.*` 不包含引擎语义，只能被对应 Adapter、Toolchain 和测试引用。
- Adapter 实现 `IRenderDevice`、`IAudioDevice`、`IInputBackend` 等后端中立契约。
- Support Pack 按目标 RID 收集 Release 原生库；Player 不读取 `extern`、Toolchain 或开发 `.lib` 布局。
- Native library 与 binding 必须由同一个固定上游 commit 生成和验证，禁止跨版本混用。

## 四、何时把能力提升为中立引擎能力

一个功能只有同时满足以下条件，才值得进入 Content、Services 或 Runtime：

1. 至少两个互不相关的 Plugin 需要共享它或互操作。
2. 能定义不依赖具体玩法模型的稳定 contract。
3. 它有必须由 Host/OS 负责的生命周期、线程、设备、资源或安全边界。
4. 缺少统一实现会导致每个 Plugin 重复解决同一类高风险问题。

若只满足“很多游戏会用”，仍不足以进入 Core。

| 能力 | 最终归属 | 原因 |
| --- | --- | --- |
| Rendering device/graph/resource | Services | GPU、帧和资源生命周期必须统一 |
| 2D/3D renderer | Plugin | 具体呈现世界观 |
| Audio device/voice/mixer | Services | 声卡、实时线程和音频时钟必须统一 |
| AudioSource/AudioListener Component | Bundled Plugin | Scene 使用方式，不是设备机制 |
| Physical input snapshot | Services | OS 事件与帧状态必须统一 |
| Input Actions/rebinding | Bundled Plugin | 玩法映射与用户策略 |
| Time domains/fixed stepping | Runtime | 所有模拟系统共享顺序与时钟 |
| Animation sampling/blending | Content | 2D、3D、UI、音频编排可共享 |
| Animator/Timeline/骨骼模型 | Plugin | 目标绑定与内容模型不同 |
| Physics backend contract | 可选 Services 家族 | 设备/世界/query 生命周期需要统一 |
| Rigidbody/Collider Component | Plugin | Scene 模型，2D/3D 语义也不同 |
| UI、Dialogue、Localization | Plugin | 明确的产品与交互世界观 |
| Save 数据模型 | Plugin | 游戏 schema |
| 沙箱化持久存储 | Services | OS 路径、安全和原子提交需要统一 |

## 五、引擎“封顶”的定义

没有任何 gameplay Plugin 时，默认引擎发行版应能：

- 启动 Editor 和 Player，创建窗口并处理应用生命周期。
- 创建隔离 EngineHost/RuntimeSession，运行空 SceneWorld。
- 提供事件、物理输入状态、时钟、Job、Coroutine、Settings、Asset 与持久存储作用域。
- 创建默认 BGFX 和 MiniAudio backend，并能在无设备时进入明确 degraded state。
- 运行空的 Rendering/Audio mechanism，加载、替换和卸载 Plugin generation。
- 从 Artifact 构建并启动不依赖源码、Compiler 或 Toolchain 的 Player。
- 诊断资源、队列、帧阶段、generation 和 backend 状态。

它不需要在没有 Plugin 时显示 Sprite、创建 Camera、放置 AudioSource 或运行 Physics。默认发行版可以
同时附带官方 2D、Audio Scene、Animation、UI 等 Plugin，使新项目开箱即用；删除这些 Plugin 后，
中立引擎能力和 Host 仍必须成立。

因此，Visual Novel 不是 Core 的封顶线。它应当是第一套验证所有机制可组合性的官方 Plugin 产品：

```text
Visual Novel Plugin
├── 2D Rendering Plugin
├── UI/Text Plugin
├── Animation/Tween Content capability + binding Plugin
├── Audio Service + Audio Scene/BGM Plugin
├── Dialogue/Localization Plugin
└── Save schema Plugin + Runtime Storage Service
```

## 六、当前代码评估

| 区域 | 当前状态 | 结论 |
| --- | --- | --- |
| Core | 稳定 | 实例化服务、严格作用域、序列化和基础调度已足够；不要继续吸收领域系统 |
| Extensibility | 强 | ModuleHost、TypeCatalog、TypeRegistry 已有 candidate/activate/complete/rollback 和 ALC retirement 主干 |
| Scripting | 强 | 显式 export、裁剪 reference assembly、compile ticket 和 reload transaction 方向正确 |
| Assets authoring | 强 | Source mount、Importer、Artifact、CAS、watcher、候选事务和异步加载已成体系 |
| Assets runtime | 已接入 | Player Database 严格且 source-free；冷路径后台读取/校验，Session 安全点提交；lease、预算、LRU 与依赖保留已有实现，完整失败矩阵以实施记录为准 |
| Scene | 稳定 | SceneWorld、统一 Behavior/System 生命周期、Play 隔离和 current-format 序列化适合作为基础世界容器 |
| Rendering | 强 | Core 不含 2D/3D 世界观；RenderGraph、Provider、resource generation 和 BGFX adapter 分层正确 |
| Audio | 已进入 Subsystem Pipeline | 中立契约、Runtime、Assets 与 MiniAudio adapter 分离；无 Scene components；原生异步解码、preload 完成/取消、Artifact 保留已接入 |
| Platform | 基础正确 | 窗口/native handle/event contract 与 SDL3 adapter 已分离 |
| Input | 基础机制已成立 | 已有 Session snapshot、键鼠瞬时状态、focus-loss 释放、脚本 façade、synthetic test boundary 与 SDL3 adapter；gamepad、text/IME、touch 待增量补齐 |
| Time | 已收口 | Session clock 提供 scaled/unscaled、pause、fixed accumulator、frame/fixed index 与 interpolation alpha |
| Runtime | Subsystem Pipeline 已成立 | EngineHost/Session 隔离，Scene、Input、Storage、Animation、Audio 与 Rendering 使用同一阶段协议和逆序释放 |
| Shell | 已恢复并中立化 | 统一 application/window/input/render/frame/exit/dispose；公开与 protected API 不含 SDL3、BGFX、MiniAudio 或 FileSystem 类型 |
| Player | 可发布 | `GamePlayerHost : Shell`；只引用 runtime catalog 和中立 contracts，发布闭包不含 Compiler、AssetPipeline、Toolchain 或 Editor |
| Editor | 扩展体系强 | `EditorHost : Shell`；通过 authoring catalog 获得编译器与 presentation，Host 不引用具体 SDL3/BGFX/MiniAudio 类型 |
| Plugins | 强 | `.iplugin`、只读 mount、依赖图、不可变 cache generation 和候选激活方向正确 |
| Build | 基本成熟 | snapshot、并行 stage、Support Pack、staging 和 atomic install 已成立；target ID、显示名称、host preference 和 UI 枚举由开放 registry 驱动 |
| Architecture Tool | 四类角色政策已落地 | 强制唯一 Solution 归属、三能力视图、adapter contract/implementation 边界、Host→Shell、native consumer、Player closure 与 tests/fixtures 分类 |

最重要的判断是：当前缺口不是“再写一套管理器”，而是统一已经存在的正确机制，使 Host 不再逐个知道它们。

## 七、首要收口：Runtime Subsystem Pipeline

### 7.1 已收口的原问题

此前 `RuntimeSession.Tick`、Player 与 Editor 各自手排 Scene、Audio、Rendering 与 scope。当前实现已由
`RuntimeSubsystemPipeline` 统一 Session 机制阶段；本节保留这些问题作为不得回退的设计依据。

这会产生四个长期问题：

1. 新增 Animation、Physics、Input state 或 Storage service 时必须修改 Player 和 Editor。
2. 作用域进入、更新、异常和逆序释放容易发生顺序漂移。
3. Plugin 无法声明 Session 级机制，只能把所有逻辑挤进 GameSystem。
4. Edit、Play、Player 很难证明使用同一机制顺序。

Host 手写 `typeof(AudioSource)`、`typeof(AudioProjectSettings)` 等类型锚点也是同一问题：发现机制没有形成
完整的部署与激活协议，Composition Root 被迫知道 Feature Model。

### 7.2 当前协议

`Inno.Runtime` 当前公开 Session-owned `RuntimeSubsystemPipeline`：

| 概念 | 稳定语义 |
| --- | --- |
| `RuntimeSubsystemId` | 开放、稳定的 feature identity |
| `RuntimeSubsystemDescriptor` | 已实现 ID、依赖、顺序、Host/Session lifetime、Required/Optional 与中立 requiredCapabilities；Optional 仅在完整补偿后允许 Unavailable |
| `IRuntimeSubsystemFactory` | 从受限 Context 创建一个 Session generation |
| `IRuntimeSubsystem` / `RuntimeSubsystem` | Session-owned、可 Dispose 的机制实例与便利基类 |
| `RuntimeSubsystemContext` | 仅提供 Events、Diagnostics、Identity、Types、LifetimeScope 和 owner metadata；不暴露 Session、Assets 或 service locator |
| `RuntimeSubsystemPipeline` | Session 启动时的 DAG 验证、scope、固定 frame 调度和逆序释放 owner |

Feature factory 目前由 Composition Root 显式提供；本体不为尚未实现的 Plugin 自动发现预留第二套入口。

固定帧顺序：

```text
Host Poll Events
    ↓
BeginFrame       清空瞬时输入、更新 clock
    ↓
DispatchEvents   将 OS/后台事件送入 Session
    ↓
FixedStep × N    Scene、Physics 等固定步长机制
    ↓
Update           Coroutine、Scene、Animation 等变量步长机制
    ↓
LateUpdate       依赖最终 Transform/状态的同步
    ↓
Presentation     Audio content sync、Rendering request/present
    ↓
EndFrame         completion、main-thread queue、统计与安全点
```

阶段必须是有限集合，禁止 Plugin 创建任意字符串 phase 使全局顺序不可推导。Feature 的局部顺序由：

1. dependency DAG；
2. 显式 priority；
3. Stable Feature ID；

共同确定。循环、缺失 required dependency 和重复 ID 在候选阶段拒绝。

### 7.3 两种生命周期不能混为一谈

应用级设备和 Session 级模拟具有不同 owner：

| 生命周期 | Owner | 示例 |
| --- | --- | --- |
| Application | `Inno.Shell` + Player/Editor derived host | 中立 application/window、render device、input source；具体实现仅存在于 catalog |
| EngineHost | EngineHost | Module、Type、Serialization、Log、Diagnostic |
| RuntimeSession | RuntimeSession/Subsystem Pipeline | Scene、Input state、Audio runtime、Animation runtime、Jobs、Coroutines |
| Presentation bridge | Application host | 把当前 Session content 投影到共享 GPU/窗口 |
| Generation | TypeRegistry/Feature candidate | Plugin provider、pipeline、mixer、extension instance |
| Frame/update | Pipeline | content scope、snapshot、command、temporary upload |
| Native callback | Adapter | MiniAudio realtime callback 等不可进入托管 Plugin 的区域 |

不要为了“统一”让 Editor 为每个 Session 创建一套 GPU device，也不要让一个 Session 拥有整个 SDL
application。Runtime Subsystem Pipeline 统一 Session 机制；Application presentation 继续由 Host 拥有，
但通过稳定 bridge 消费被选择的 Session，而不是读取 Feature Model 类型。

### 7.4 Feature 激活与失败

Feature generation 必须复用当前 ModuleHost/TypeCatalog 的事务思想：

```text
Discover
  → Build complete candidate
  → Validate dependency/capability/resource requirements
  → Prepare neutral state
  → Activate at frame boundary
  → Restore by Stable ID
  → Complete and retire previous generation
```

- candidate 任一步失败：保留 last-good generation。
- required feature 初次启动失败：Session 启动失败，不能偷偷跳过。
- optional feature 初次失败：进入明确 unavailable/degraded state 并发布结构化诊断。
- 已运行 subsystem 的单帧异常：当前政策统一传播并终止该轮执行，由产品请求 Session 退出；Optional 只影响初次启动，不自动吞掉帧异常。
- rollback 必须撤销 candidate 已完成的所有外部注册。
- complete 只做旧 generation 退休和 cleanup，不再执行可失败的发布。
- persistent state、History 和 Asset 中只保存 Stable ID 与中立 bytes，不保存 Type、实例、delegate。

Runtime Subsystem 不是 Service Locator。`RuntimeSubsystemContext` 只暴露固定的窄 contract，禁止
`IServiceProvider/GetService(Type)`。领域间协作使用显式 capability interface 或中立 snapshot。

## 八、各领域的干净目标

### 8.1 Core 与 Extensibility

Core 当前已经接近封顶。后续工作以守边界为主：

- 不把 Animation、Physics、Rendering、Audio 或 UI 塞入 Core。
- 保持所有真实状态实例化；静态 façade 只能解析当前 scope，不能拥有状态。
- Event、Job、Log 等跨线程队列增加可配置预算、overflow policy 和统计，避免大型项目无界增长。
- TypeRegistry 继续拥有 complete immutable snapshot；Attribute 只保存声明性 metadata。
- Domain activator 可以支持固定白名单的 constructor dependency，但禁止任意容器解析。
- generation retirement 必须持续保留 ALC 可回收测试。

### 8.2 Runtime 与 Scene

`RuntimeSession` 应只拥有 Session 基础服务和 Subsystem Pipeline，不引用 Rendering、Audio、Platform
adapter 或 Editor。SceneWorld 保留为内建基础世界容器，但应作为固定 Runtime participant 接入统一阶段，
而不是阻止其他机制参与帧顺序。

Scene 的永久边界：

- `GameObject`、`GameComponent`、`GameBehavior`、`GameSystem`、`Transform` 属于 Scene。
- Camera、Renderer、AudioSource、Listener、Rigidbody、Animator、UI Element 不属于 Scene。
- 跨域集成使用 `Inno.<Domain>.Scene` bridge 或 Plugin provider。
- Scene serialization 只识别 stable type/identity 和中立 state，不维护领域类型名单。

Gameplay Plugin 的逐对象/逐场景逻辑使用 GameBehavior/GameSystem，跨场景领域行为使用中立
Provider/Feature 扩展点。RuntimeSubsystem 是引擎贡献者增加正式 Session 基础设施的入口，
不导出给 gameplay scripting，也不成为第二套 gameplay update API。

### 8.3 Assets：补齐 Player 长期运行能力

Authoring AssetPipeline 与 Player `AssetDatabase` 当前共同实现 `IAssetResidency`。脚本通过 `Assets`
取得 `AssetLease<T>`、`ArtifactLease` 和 `RetentionScope`；旧的 `Load/TryLoad` 明确表示 Session 级 pin。

当前已完成：

- Runtime lookup 提供显式异步 lease；Authoring 的并发 load 继续 coalesce，Runtime 物化遵守 Session owner。
- cancellation 不破坏其他调用者和已提交 canonical instance。
- 增加显式 runtime residency/retention lease，供 Rendering、Audio、Streaming World 等机制使用。
- canonical metadata/object cache 与大型 Artifact residency 分离。
- 预算回收保留显式 lease、Session pin 和已加载 dependency closure。
- `AssetResidencyStatistics` 暴露预算、resident、materialized 与 leased 数量。
- Runtime 永远不扫描 Source、不运行 Importer、不写部署内容。

后续大型项目优化是按 decoded texture、geometry、audio、shader 分别增加 subsystem budget 和预取统计；
它不能重新建立一个含糊的全局资源 Manager。引擎机制始终依赖显式 lookup/residency，不调用静态 façade。

### 8.4 Input

`Inno.Core.Input` 只保存物理 code/value type；独立 Input Service 已建立：

| 项目 | 职责 |
| --- | --- |
| `Inno.Input` | `IInputService`、`InputSnapshot`、device handle、脚本 `Input` façade |
| `Inno.Input.Runtime` | 每 Session 状态、pressed/released/down、mouse delta/wheel、focus 与 device generation |
| `Inno.Adapter.Input.Sdl3` | keyboard/mouse/focus 的事件累积与 Session backend 隔离 |
| Bundled Input Actions Plugin | action map、binding、rebinding、player assignment、UI navigation |

当前 Physical Input Service 覆盖：

- 键盘 down/pressed/released 与 repeat 分离。
- 鼠标 position/delta/wheel/button 和 window/focus identity。
- focus loss 时确定性释放 held state。
- Frame snapshot 不受同帧查询顺序影响。
- 测试可注入的 synthetic backend，不依赖 SDL 或真实设备。

下一增量是 gamepad hotplug/generation handle 与 UTF text/IME；Action、rebinding 和导航不进入 Core，因为
它们是可替换的输入解释策略。

### 8.5 Time：从四个 float 扩展为时钟域

Runtime clock 当前提供：

- scaled 与 unscaled time/delta。
- `timeScale` 和明确 pause 语义。
- fixed accumulator、fixed step count 与 interpolation alpha。
- frame index 与 monotonic duration。
- Audio DSP time 保持独立，不从 scaled game time 推导。

Clock 由 Session Subsystem Pipeline 一次更新，其他机制只读取 snapshot。禁止每个机制自行创建 Stopwatch，
也禁止 Animation、Physics 和 Audio 各自解释 pause 顺序。

### 8.6 Rendering

Rendering 当前是最接近目标形态的领域，应作为其他机制的样板：

- `Inno.Rendering` 保持 backend-neutral。
- `Inno.Rendering.Runtime` 拥有 frame/resource/provider generation。
- Scene 的 `SceneContentSource` 创建 `Inno.References.ContentReadScope`；不再保留独立 Rendering.Scene 薄桥项目。
- `Inno.Adapter.Rendering.Bgfx` 是 Default Adapter。
- 2D、3D、Camera、Light 等只在 Plugin。

已经完成的收口：

- `FileRenderTargetArtifactProvider` 已移至 `Inno.Rendering.Runtime`，`Inno.Runtime -> Inno.Rendering` 反向引用已删除。
- Rendering attach/before/render/after/detach 已接入统一 Runtime Subsystem lifecycle，Player 不再手排。
- 保留一个 RenderGraph 和 Shader IR；新渲染模型不得创建旁路。
- Resource service 按 shader/material/persistent resource/residency 职责拆分，而不是按 Helper 名称拆文件。
- 补齐 CPU/GPU timing、allocation、upload、cache hit/eviction 和 graph compile 统计。

### 8.7 Audio

Audio 的最终结构：

| 项目/包 | 最终职责 |
| --- | --- |
| `Inno.Audio` | Clip/Voice/Bus/Mixer/Spatial/Content/Provider 的全部中立 contract 与 façade |
| `Inno.Audio.Runtime` | cache、voice budget、device recovery、provider generation、graph 安装与 completion |
| `Inno.Audio.Assets` | WAV/FLAC/MP3 authoring importer 和 Artifact |
| `Inno.Adapter.Audio.MiniAudio` | 唯一默认 native adapter |
| Bundled Audio Scene Plugin | AudioSource、AudioListener 与 Scene provider |
| `Inno.Editor.Audio` | preview、Edit/Play Session audio generation 和诊断 |

`ContentReadScope` 是 Audio/Rendering 共用的 Identity-backed 输入边界；领域 snapshot、
Provider Attribute/Context/Base Type 保持 `Inno.Audio`。后续不得重新建立各领域自己的 live object 索引。

`AudioSource` 和 `AudioListener` 不属于本体，未来由官方 Bundled Plugin 提供。低层
`AudioListenerHandle`、`AudioListenerState` 和 spatial voice 参数仍留在 Audio Service；移出的只是
“Scene 里用 Component 表示声音”的具体模型。

已经删除 Player/Editor 的 `typeof(AudioSource)` 与 Importer/Settings 类型锚点；部署 closure 与显式 factory
负责装配。仍需继续：

- 已实现 MiniAudio 原生异步资源准备；`PreloadAsync` 在 Ready 后完成，支持取消，准备中的 Voice 不误报 Playing。
  Artifact 校验/查找的进一步 IO 成本优化不等同于 decoder 尚未异步。
- 将 AudioRuntime 按 ClipCache、VoiceScheduler、MixerGeneration、DeviceRecovery、ContentSync
  拆成内部 owner，外部仍只有一个 `IAudioService`。
- 明确 realtime thread 永远不执行 managed Plugin、reflection、Asset IO、lock、log 或 allocation。
- device lost/recovery、muted mode、stale handle 和 completion 继续保持显式状态。

静态 `Audio` façade 不破坏封装：它无状态，只解析当前 `IAudioService` scope。应保持的规则是
“脚本 façade 简单、基础设施显式依赖 service”，而不是禁止所有静态便利 API。

### 8.8 Animation

Animation 已作为 Content capability 实现且不进入 Core。2D、3D、UI、Camera、Audio automation 和 Visual Novel
可以共享一致的时间采样、事件与 blending，而不引入某种 Scene 世界观。

当前家族：

| 项目 | 职责 |
| --- | --- |
| `Inno.Animation` | clip、track、keyframe、curve、event、playback handle、binding ID 中立 contract |
| `Inno.Animation.Runtime` | player、layer/blend、cache、clock domain、event dispatch、generation |
| `Inno.Animation.Assets` | 当前 `.ianim` Animation Clip importer 与 Artifact |
| Bundled Animation Scene Plugin | Scene content bridge 和 stable target path；不属于本体项目 |
| Bundled binding Plugins | Transform/property、Sprite、skeletal 2D/3D、UI、Audio parameter |
| Timeline/VN Plugin | cutscene、dialogue sequencing、branching，不属于中立 Animation capability |

中立 Animation capability 不应通过反射每帧写任意 property。Binding Plugin 在 generation 构建时把 Stable
Binding ID 编译成可执行 binding plan；运行时只消费已验证 plan。Plugin reload 后重新解析 plan，
persistent clip 仍只保存 Stable ID 与中立数据。

### 8.9 Physics、UI、Networking 等后续系统

这些系统不阻塞本轮架构封顶：

- Physics：需要时建立可选 `Inno.Physics2D` / `Inno.Physics3D` mechanism family 和 backend adapter；
  Rigidbody/Collider/Joint Scene 类型放 Bundled Plugin。不要强行用一个最低公共 API 合并 2D/3D。
- UI：布局、Widget、Canvas、主题、交互模型属于 Plugin；Input 的 text/IME 和 Rendering contract 属于机制。
- Text/Font：font asset、shaping、atlas 与 render provider 可以是官方 Plugin 家族。
- Networking/VOIP：transport/device contract 可以成为可选 Service，replication/game protocol 属于 Plugin。
- Localization、Dialogue、Quest、Inventory、Save schema、AI、Navigation 都是 Plugin。

只有当这些领域出现真实的跨 Plugin 互操作需求时，才提炼中立 capability；禁止预先把猜测的抽象放入 Core。

### 8.10 Runtime Storage

Session 当前通过独立 Storage Service 获得稳定、沙箱化的存储边界：

- `IApplicationStorage`：只允许 application-local logical path。
- atomic read/write/replace、directory enumeration 和 cancellation。
- `StorageRuntimeFactory` 让 Edit、Play、Player 明确选择各自 root。
- 脚本 façade 无状态，具体 Save schema、slot、autosave、cloud sync 由 Plugin 实现。

默认 `FileSystemApplicationStorage` 拒绝 rooted/parent key、symlink/reparse point，并使用同目录临时文件完成
原子 replace。原始绝对路径只存在于 Composition Root；Core 不定义游戏存档格式。

### 8.11 Build、Player 与 Backend

当前 Build 的 revision 检查、immutable staging、Support Pack 和 atomic install 应保留。已完成：

- `BuildProfile.Validate` 只验证 Target ID 格式，不硬编码 macOS/Windows。
- `BuildPipeline` 从已注册 `IGameBuildTarget` 解析 target；unknown target 在组合层明确报错。
- Editor 枚举 registry target，并使用唯一 host-preferred adapter 或稳定排序 fallback 作为默认值。

后续增量：

- Support Pack manifest 声明 backend、RID、native closure 和 capability，Build 验证完全匹配。
- Player composition root 可以选择默认 adapter，但不得知道具体 gameplay/Scene component 类型。
- 长时间 Build 可增加 Project Generation Lease，减少频繁变更导致的重试和旧资源退休压力；当前多次
  revision 验证已经保证正确性，因此这属于伸缩优化而不是重写理由。

### 8.12 Editor

Editor Application 是默认发行版的 Composition Root，因此引用默认 Panel、Build Target、
`IAuthoringAdapterCatalog` 与中立 presentation/audio/rendering contract 是合理的。它不能直接引用或公开
BGFX、SDL3、MiniAudio、FileSystem 或具体 ImGui bridge；这些实现只能由默认 catalog 在 Adapter 边界内创建。

Editor 在磁盘与 Solution 中都进行二级分组：`host` 只放 Application composition root；`framework` 放共享
Editor contract/runtime；`features` 放 Audio、Rendering、Scene、Scripting、PlayMode、Exporting 等领域集成；
`presentation` 只放 ImGui editor runtime；`panels` 放可发现的具体 Panel。项目不得直接挂在 `editor` 根节点。

目标：

- Editor Module/Panel 继续通过 ExtensionCatalog 自动发现。
- Edit/Play 使用同一个 Runtime Subsystem Pipeline contract；Play 仍创建隔离 RuntimeSession。
- Editor Rendering 作为 application-owned presentation bridge 切换 Edit/Play content。
- Editor Audio 继续为每个 Session 拥有独立 runtime generation，但注册和 scope 由 Session pipeline 协调。
- domain-specific preview/inspector/window 放对应 Editor module 或 Plugin，不进入 Application。
- History、Workspace state、Selection、Reload transaction 继续只有一套 owner。
- Application 不使用 `typeof(T)` 强制加载业务类型；部署 manifest 和显式 default module list 负责 assembly closure。

## 九、生命周期与线程政策

### 9.1 所有权

每个有状态对象必须能回答“谁创建、谁更新、谁停止、谁 Dispose”：

| 对象 | 唯一 owner | 退休时机 |
| --- | --- | --- |
| Platform application/window | Application Host | 应用关闭 |
| Native device | Default Adapter owner | Host 关闭或显式 device replacement |
| Module/Type/Serialization generation | EngineHost | reload complete 或 Host 关闭 |
| Runtime Subsystem | RuntimeSession pipeline | candidate replacement 或 Session 关闭 |
| Scene object | SceneWorld/GameScene | destroy、unload、reload 或 Session 关闭 |
| Asset canonical object | Asset lookup/residency owner | safe-point eviction 或 lookup 关闭 |
| GPU/Audio handle | 对应 Runtime mechanism | generation replacement、unused sweep 或 device loss |
| Frame snapshot/command | Frame pipeline | FrameEnd |
| Plugin extension instance | Registry generation | complete/rollback/retirement |

构造失败必须只释放已经取得所有权的资源；正常关闭必须逆序执行并聚合 cleanup failure。Dispose 应幂等，
但重复 Enter scope、重复 Attach 或错误顺序不能依赖“碰巧无害”。

### 9.2 线程

| 线程区域 | 允许 | 禁止 |
| --- | --- | --- |
| Owner/Main thread | Scene mutation、registry activate、device control、Editor state | 长时间同步 IO/compile/decode |
| Worker | 基于 immutable generation 的 import/compile/decode/analysis | 直接修改 Scene、Editor UI 或 active registry |
| Render submission | immutable graph/command/resource request | Plugin 保留 frame object、无界分配 |
| Native audio realtime | 预构建 native graph 和 lock-free 数据 | managed Plugin、reflection、Asset IO、lock、log、allocation |
| Watcher/callback | enqueue 中立 change signal | 直接 commit catalog、Plugin 或 UI state |

Worker 结果只能在 owner-thread safe point 提交。CancellationToken 的 owner 是发起 operation 的 Host；
Dispose 必须停止接受新工作、取消可取消工作、等待已接受工作收敛，再释放 generation。

### 9.3 Hot reload

所有可热重载领域统一遵循 [Identity、可恢复引用与热重载强制标准](IDENTITY_REFERENCE_RELOAD_STANDARD.md)：

1. 从 immutable source/generation 构建完整 candidate。
2. 在 candidate 内验证 Stable ID、依赖、资源与 capability。
3. 在 frame boundary capture 当前中立状态。
4. 原子 activate Module、Type、Serialization、Assets、Settings、Feature registry。
5. 通过 persistent ID/Stable Type ID/中立 bytes 恢复状态。
6. 退休旧 generation，释放 Host、Scene、Asset、Editor、callback 和 native bridge 的全部强引用。
7. 执行 Full GC → pending finalizers → Full GC，并等待所有退休 collectible ALC 的弱 monitor 完成。
8. 只有旧 generation 已确认不可达后才报告 reload Success；任一步 pre-commit 失败 rollback 并保持 last-good。

GC verification 可以跨多个 Editor frame 推进，但不能在 Pending 时消费下一 candidate、进入 Play、Build 或 Export。
达到诊断阈值仍有 ALC 存活时必须抛出 unload exception 并进入 Faulted，禁止清空 monitor 后把 reload 当作成功。
Missing Asset、Script Type、Plugin 或 extension 继续保留 persistent ID、Stable ID、结构位置和中立 bytes；
恢复后通过同一 reference recovery transaction 原子重建，Undo/Redo 不丢弃暂时不可用的记录。

Backend Adapter 和 native device 默认不热重载。若未来需要替换 device，使用显式 generation replacement，
而不是把 native library 放进 collectible Plugin ALC。

## 十、Backend、Plugin 与 Native 的固定政策

### 10.1 三者不可混称

| 名称 | 面向谁 | 可引用 Native | 可热重载 | 可进入 `.iplugin` |
| --- | --- | --- | --- | --- |
| Backend | 中立引擎能力的具体底层实现 | 是 | 默认否 | 否 |
| Adapter | Backend/OS 与中立 contract 的连接层 | 是 | 默认否 | 否 |
| Plugin | 游戏模型、内容和 managed extension | 否 | 是 | 是 |
| Bundled Plugin | 官方默认分发的 Plugin | 否 | 是 | 是 |

### 10.2 当前 native policy

- `.iplugin` v1 只包含内容与 managed scripts。
- SDL3/BGFX/MiniAudio 等 native binary 只来自 Engine Support Pack。
- Plugin 不得在 Assets 中夹带 dylib/dll/so 并尝试自行加载。
- Plugin 不得把 native pointer、backend enum 或 adapter type 持久化或暴露到脚本 API。

未来若确实需要第三方 native Plugin，应单独设计“Native Companion”协议：

- target/RID 精确匹配。
- 安装时签名/hash/closure 校验。
- 应用启动前选择，不参与 managed hot reload。
- 与 managed Plugin 通过稳定 C ABI 和中立 handle 通信。
- 不复用普通 `.iplugin` 的 Assets mount 路径。

在该协议完成前，禁止以临时 DllImport 或任意路径加载绕过政策。

## 十一、统一 API 与代码风格

### 11.1 类型后缀

| 后缀 | 语义 |
| --- | --- |
| `Id` | 可持久化、跨 generation 的稳定逻辑身份 |
| `Handle` | 不透明、generation-scoped runtime identity |
| `Asset` | 可序列化的创作/项目输入 |
| `Descriptor` | 创建资源所需的完整不可变输入 |
| `Options` | Host policy 或一次 operation 配置 |
| `State` | 可观察或可替换的当前状态值 |
| `Snapshot` | 某一 frame/generation 的不可变观察 |
| `Context` | 一次调用或短生命周期 operation 的能力集合 |
| `Service` | stateful orchestration contract |
| `Device` | backend primitive contract |
| `Provider` | 将外部模型转换成中立提交的扩展点 |
| `Adapter` / `Bridge` | 两个边界之间的转换，不拥有上层模型 |
| `Host` | 顶层生命周期 owner/composition root |
| `Registry` | 自动发现并原子发布完整 generation snapshot |
| `Transaction` | Prepare/Activate/Complete/Rollback 协议 |

不要把 `Manager`、`Helper`、`Util`、`Internal` 当作无法描述职责时的默认命名。

### 11.2 Façade

`Audio`、`Assets`、`Time`、未来 `Input`/`Storage` 可以保留 Unity 风格静态入口，但必须满足：

- 不持有 process-global mutable state。
- 只通过 AsyncLocal strict-LIFO execution scope 解析当前显式 service。
- 无 scope 时明确抛出 `InvalidOperationException`。
- 引擎、Host、Editor 和测试依赖显式 interface/instance，不调用 façade。
- façade 只保留最常用操作；复杂生命周期通过 handle/service。

这就是最小封装原则：最小的是“公开语义和状态入口”，不是“公开成员数量越少越好”。

### 11.3 Extension

- 每个扩展点只有一个 Attribute、一个 base/interface 和一个 domain registry owner。
- Attribute 只保存 Stable ID、priority、capability 等 metadata。
- 不建立中央类型名单、switch 或 Host `typeof(T)` anchor。
- Registry candidate 必须完整验证后原子替换。
- extension instance、Type 和 delegate 只能存在于 generation snapshot。
- constructor injection 只允许 domain activator 的固定窄白名单；禁止通用 service locator。

### 11.4 错误语义

统一区分：

- 参数/格式错误：抛明确异常。
- 查询 miss 或 stale handle：`Try*` 返回 false。
- candidate 失败：保留 last-good 并发布结构化诊断。
- required service 首次失败：阻止 Session/Build 启动。
- 可接受设备缺失：进入明确 degraded state，例如 Muted Audio。
- cleanup 失败：继续释放其余 owner，最后聚合报告。
- 禁止 silent fallback、catch-all 后无诊断和返回伪造成功。

## 十二、大型项目必须提前锁定的政策

### 12.1 资源与内存

- 所有 cache 都必须有 owner、key、generation、budget、eviction 和统计。
- Asset object、Artifact bytes、decoded CPU data、GPU resource、Audio decoder 是不同 residency。
- 大型内容通过 Artifact streaming，不进入通用 serialization payload。
- Plugin/Script old generation 必须可观察地退休；测试检查 collectible ALC 弱引用最终失效。
- Frame snapshot 不得被异步任务或静态集合保留。

### 12.2 队列与背压

当前 EventDispatcher、main-thread Job queue、LogRouter 等使用队列。大型项目需要为每条通道定义：

- 是否有界。
- overflow 时 drop/coalesce/block/fail 哪一种。
- dropped count 和 high-water mark。
- 哪些事件可以合并，例如 mouse motion、resize、asset watcher signal。
- 哪些事件绝不能丢，例如 quit、device lost、build completion。

不能等 OOM 或一帧 Drain 数十万项时才把问题当作性能 bug。

### 12.3 可观测性

增加统一的实例化 diagnostics/profiling mechanism，至少覆盖：

- frame stage CPU time 和 fixed-step debt。
- Job queue/worker utilization/main-thread continuation。
- Asset load latency、cache hit、residency 和 eviction。
- Rendering graph compile、pass、draw、dispatch、upload 和 GPU timing。
- Audio voices、stealing、decode、stream underrun 和 device recovery。
- Plugin/Script candidate build、activate、rollback、ALC unload。
- Event/Log queue depth 和 drop count。

Profiler 不应通过静态全局单例抓取状态；它订阅 Host/Session 的中立 metric snapshot。

### 12.4 确定性与测试

- 稳定排序始终以 dependency、priority、Stable ID 收尾。
- Clock、Input、Device、Storage 和 Artifact provider 都应有 public replaceable boundary。
- 测试只通过 public contract，不使用 IVT 或 non-public reflection。
- Headless/no-device path 必须与正常 path 使用同一 Runtime mechanism。
- macOS ARM64 与 Windows x64 都运行 native smoke 和 Player E2E。

## 十三、职责中心的拆分策略

优先按所有权与事务拆分，而不是按行数：

| 当前职责中心 | 建议内部 owner |
| --- | --- |
| `AssetLoader` | source reconciliation、import execution、canonical cache、artifact/dependency lifecycle |
| `AssetPipeline` | public orchestration、mount transaction、async operation、watcher commit |
| `RenderResourceService` | shader/pipeline generation、material resolution、persistent resource、residency |
| `AudioRuntime` | clip cache、voice scheduler、mixer generation、device recovery、content sync |
| `EditorSceneWorkspace` | document owner、runtime materialization、history adapter、reload/state restore |
| `ScriptReloadHost` | request/debounce、compile operation、activation transaction、unload verification |
| Editor Application | boot composition、frame host、shutdown owner；领域 setup 下沉到明确 module/factory |

拆分后的 internal 类型必须使用真实功能名称，并留在同一 bounded context。不要为了缩短文件创建几十个
无独立 invariant 的 Helper，也不要把 internal 实现扩大为 public。

## 十四、Architecture Tool 应新增的强制规则

在现有 cycle、IVT、global using、native、Player closure 和 test Solution Folder 规则上增加：

1. `Inno.Runtime` 不得引用 Rendering、Audio、Platform adapter、Editor、Build 或 Native。
2. `Inno.<Domain>` contract 不得引用自己的 Runtime、Scene、Editor 或 backend adapter。
3. 只有 `*.Scene` bridge 或 Bundled Plugin 可以同时引用 Scene 和领域 contract。
4. `Inno.Scene` 不得引用 Camera、Renderer、AudioSource、Listener、Physics、Animation 等模型程序集。
5. Player/Editor composition root 不得用 `typeof(T)` 锚定 gameplay/feature model。
6. Plugin-facing contract 的源码必须编译在其宣称的 contract assembly，不得只伪装 namespace。
7. Native binding 只允许对应 Adapter、Toolchain 和 tests 引用。
8. 非 Native public/protected API 不得泄漏 backend type、pointer 或 native enum。
9. Open ID 不得在 Core/中立 capability 用封闭 switch 验证“支持名单”；支持性由 registry/capability 判断。
10. RuntimeSubsystem 不进入普通持久状态，generation object 不得进入 native realtime callback。
11. Host、Runtime、Editor 的 ProjectReference 继续按 public signature 与 implementation dependency 分组。
12. 所有新增项目必须进入 Solution 正确分类并拥有 Wiki 项目页。
13. 跨 UI、callback、queue 和 frame boundary 的 live object 寻址必须经过 Identity；runtime ID 不得进入持久状态。
14. ImGui Editor DragDrop payload 只能传 Identity runtime ID，禁止随机 token 回查 managed source object。
15. Missing reference/state 不得保存退休 generation 的 `Type`、object 或 delegate，也不得因暂时不可解析改写为 null。
16. reload-safe History 只能保存 persistent ID、Stable ID、结构数据和中立 bytes；Missing 只能形成可恢复 barrier。
17. reference-aware serialization 必须使用 owner 组合出的完整 context，禁止领域自行从 `SerializationContext.empty` 拼装不完整 resolver。
18. reload Success path 必须经过 completed GC unload barrier；Pending monitor 不能因 timeout 被遗忘。

工具规则应该检查稳定且低误报的结构，不用脆弱的全文关键词代替语义分析。复杂 public API 泄漏可使用
Roslyn/metadata inspection，项目依赖继续使用 MSBuild/XML graph。

## 十五、实施记录与后续顺序

### 阶段 A：边界与机器规则（已完成）

- Solution 与物理目录均采用 Foundation → Content/Services/Runtime → Adapters → Composition，并在层内按领域二次归类。
- Architecture Tool 已检查唯一 Solution 归属、概念层引用方向、tests/fixtures、Native consumer、Host
  `typeof` anchor、Reference Serialization Context 和删除项目。
- `FileRenderTargetArtifactProvider` 已移入 `Inno.Rendering.Runtime`，Runtime 反向引用已删除。
- 本 Overview 与 Identity 专项标准已成为架构入口。

### 阶段 B：Runtime Subsystem Pipeline（静态 Session generation 已完成）

- 已建立 descriptor/factory/context/pipeline、固定 frame stage、DAG 校验、scope 和逆序释放。
- Scene、Coroutine、Job、Input、Storage、Animation、Audio 和 Rendering 已进入同一 Session contract。
- Feature factory 由 Composition Root 显式注册；本体不以 `typeof` 或中央名单发现 gameplay model。
- 当前 pipeline 在 Session 启动时冻结。未来如确有“运行中替换 Session mechanism”的需求，必须复用
  candidate/last-good/rollback/GC barrier，不得给现有静态 pipeline 虚构热替换语义。

### 阶段 C：现有机制迁移（已完成）

- Audio 与 Rendering 已有正式 Runtime Subsystem；Settings、Assets、Time、Diagnostics 和领域 façade 由
  Session/Feature scope 统一进入与退出。
- Player/Editor 已删除 AudioSource、Settings、Importer 等业务 `typeof` anchor。
- Host 只装配 adapter、factory 与明确的产品模块，不直接调度 gameplay component。

### 阶段 D：Audio 边界（结构完成，性能增量待做）

- Content/provider contract 已属于 `Inno.Audio`；`Inno.Audio.Scene`、`AudioSource`、`AudioListener` 已删除。
- Editor、Play、Player 与 no-device 路径继续使用同一 backend-neutral contract。
- 已完成原生异步 decoder preparation 和请求/Artifact lifetime 分离；ClipCache、VoiceScheduler、
  MixerGeneration、DeviceRecovery、ContentSync 的完整内部 owner 拆分仍未关闭，不改变公开 `IAudioService`。

### 阶段 E：通用引擎能力（主干完成，设备与观测增量待做）

- 已完成：scaled/unscaled Time、pause/fixed-step、键鼠 Input snapshot、Runtime Asset lease/residency/budget/LRU、
  Runtime Storage、Animation contract/assets/runtime。
- 待做：gamepad hotplug、UTF text/IME、touch，以及 Event/Log/Job/Rendering/Audio 的统一 queue budget 与
  metric snapshot。

### 阶段 F：Default Distribution 与 Visual Novel（本次明确不实现 Plugin）

后续由独立 Plugin 工作完成 2D Rendering model、Audio Scene model、Tween/binding、UI/Text/Font、Dialogue、
Localization、Visual Novel 与 Save schema。完成标准仍是：项目只由 Plugins + Assets 构成，不修改引擎源码；
删除 VN Plugin 后，Editor/Player 和其他 2D/3D 模型仍可运行。

### 阶段 G：大型项目与 3D（按真实需求实施）

Physics2D/Physics3D mechanism、3D/PBR/shadow/lighting Plugin、Streaming World、LOD、Navigation 和 Networking
只有在真实需求与 profile 证据出现后才立项，不预先污染 Core。

## 十六、最终验收清单

勾选项表示当前源码已经具备且应由测试或 Architecture Tool 持续守护；未勾选项是明确后续工作，不能在
Wiki 中描述为已完成能力。

### 分层

- [x] `Inno.Core.*` 不含任何 gameplay、backend、Editor 或 Build 世界观。
- [x] 中立 capability contract 不反向依赖具体 implementation、Composition 或 Native。
- [x] Default Host 只通过中立 enum/catalog 选择 backend，且不知道实现类型或 Plugin model 类型。
- [ ] 新 gameplay 功能只增加 Plugin/Asset，不改中央 switch。

### 生命周期

- [x] Edit、Play、Player 使用同一个 Session frame stage contract。
- [ ] Feature candidate 失败保留 last-good，初始 required feature 失败阻止启动。
- [x] 所有 scope 严格 LIFO，一帧只进入一次组合 scope。
- [x] Dispose 逆序、幂等、聚合 cleanup failure。
- [x] retired collectible ALC 有完成式 GC barrier 与弱引用测试；其他 provider/resource/handle 继续由各领域生命周期测试守护。

### Identity、Missing 与恢复

- [x] Editor ImGui DragDrop 和已迁移 live-object 索引走 domain-qualified runtime Identity。
- [x] runtime ID 不进入 Scene、Asset、Settings、Plugin、History 或其他持久状态。
- [ ] Asset、Script Type、Plugin 和 extension 暂时缺失时保留 identity、Stable ID、结构与中立 bytes。
- [ ] 同一目标恢复后自动重建；Missing 期间 Undo/Redo 记录不丢失、不跳过。
- [x] Scripting reload 只有在所有退休 ALC 已被 GC 确认回收后才允许下一 generation，否则进入 Faulted。

### Backend 与 Plugin

- [x] 只有 Adapter/Toolchain/tests 引用 Native。
- [x] `.iplugin` 不包含 native binary。
- [x] Plugin 可缺席、卸载、替换，不被 Host `typeof` anchor 固定。
- [x] native realtime callback 不执行 managed Plugin。

### 大型项目

- [x] Runtime Asset 支持异步、residency、budget 和 eviction。
- [ ] Event/Log/Job continuation 有 queue policy 与统计。
- [ ] Rendering/Audio/Asset/Plugin generation 有 metric snapshot。
- [ ] 长任务可取消，结果只在 safe point 原子提交。

### 产品能力

- [x] 空 Plugin 集可以启动空 Editor/Player。
- [ ] 官方 Visual Novel 只通过 Plugin + Assets 完成。
- [ ] 2D 与未来 3D Plugin 可同时使用同一 Rendering/Scene/Animation 底座。
- [x] 替换 backend 不改变游戏脚本 API。
- [ ] macOS ARM64 与 Windows x64 Build、native smoke 和 Player E2E 全部通过。

## 十七、现在明确不做的事

- 不重写已经正确的 ModuleHost、TypeCatalog、Serialization、Asset Artifact、RenderGraph 或 Build staging。
- 不把所有现有程序集合并成“大 Core”。
- 不为旧 API、旧 namespace 或旧 schema 增加兼容层。
- 不先实现完整 DAW、完整 3D Renderer、完整 Physics 或网络栈。
- 不用通用 DI container/service locator 掩盖依赖。
- 不让 Plugin 直接控制 native device 或 OS callback。
- 不因某功能“常见”就把它提升为 Core。
- 不为 DragDrop、History、Assets、Scene 或 Plugin 分别保留平行 object token、Missing resolver 或 reload completion 协议。

最终原则可以压缩为一句话：

> Core 提供不带世界观的原语；Content、Services 与 Runtime 统一高风险生命周期；Default Adapter 保证开箱即用；
> Plugin 定义游戏是什么。

## 十八、2026-09-06 本体收口验收记录

- `InnoEngine.sln` Debug build：0 warnings、0 errors。
- 完整 managed test suite：673 passed、0 failed、0 skipped。
- `Inno.Tooling.Architecture`：通过 Solution 唯一归属、概念依赖方向、Native consumer、Player closure、
  Scripting API、Identity/reference/reload 禁止模式等规则。
- macOS ARM64 Support Pack：成功生成，并包含唯一 Release MiniAudio native library。
- macOS ARM64 Player E2E：从临时 Project 导入 Audio/Scene、构建 source-free Player、启动 Metal/BGFX，
  完成 3 帧并正常 shutdown。
- Windows x64：项目、Support Pack、native build 与 E2E matrix 已配置；真实进程验收仍由 Windows runner 完成。

本记录只证明上述当前平台与测试输入；未勾选的后续能力不会因本次 build 成功而自动视为完成。
