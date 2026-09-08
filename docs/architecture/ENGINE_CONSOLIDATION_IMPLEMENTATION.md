# 引擎本体统一收口：实施与验收记录

[架构索引](README.md) · [Architecture Overview](ENGINE_ARCHITECTURE_OVERVIEW.md) · [Identity / Recovery / Reload 标准](IDENTITY_REFERENCE_RELOAD_STANDARD.md)

本页记录 2026-09-07 用户批准的完整收口范围。目标不是新增玩法 Plugin，而是让所有引擎子系统使用统一的生命周期、诊断、引用恢复、默认装配和封装标准。实现状态与验证状态分别记录；没有运行的验证不得标为通过。

**当前状态入口：[2026-09-08 实现交付与集中验收](ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md)。** 下方实施表与执行日志保留前序阶段事实；2026-09-08 已补全部剩余实现，验证另行集中记录，不沿用旧通过计数。

## 最终架构

- 磁盘与 Solution 同时采用 `foundation`、`content`、`services`、`runtime`、`adapters`、`composition`；中立能力分为 content/services/runtime 三个职责区，不恢复 mechanisms 巨型分组。
- Shell 是通用应用 Host；Core Layer/LayerStack 是局部顺序原语；RuntimeSubsystem 是正式引擎子系统；领域 Feature 只贡献 Render Pipeline / Audio Mixer 等领域行为。
- `Inno.Runtime.Contracts` 提供不依赖 Scene、Assets、领域服务和 backend 的运行协议；领域 Runtime 组合 Device，直接实现 service 与 subsystem，不保留纯转发 RuntimeSubsystem 包装。
- Host / Session / extension generation / frame / native execution 各有明确 owner。Editor 与 Play 共用的 GPU device 只由 Host 释放，每设备每帧只提交一次。热重载只替换扩展 generation，不重建稳定设备。
- Diagnostics 统一进入 Core DiagnosticHub；Reporter 是生产者，Sink 是消费者。领域结果可以保留必要结构，但不能维护第二套诊断状态 owner。
- Execution scope 必须严格 LIFO、可撤销、清除已流入异步任务的 service 强引用；生命周期跟踪取消、任务、订阅与逆序释放。
- `Inno.References` 提供 Identity-backed ContentReadScope；Scene 贡献中立内容源；Audio/Rendering 不引用 Scene，不增加 AudioSource/Listener/Camera 等本体模型。
- 所有候选与恢复使用同一个 GenerationCoordinator 和事务 gate；旧 ALC 经 Full GC / finalizers / Full GC / weak verification 后才成功；不可恢复的回滚或退休失败进入 Faulted。
- 子系统本地声明经 `Inno.Runtime.Generators` 生成强类型装配；`Inno.Engine.Default` 组合子系统，`Inno.Adapter.Default` 提供具体 backend 工厂，Editor/Player 不手写两份领域名单。
- Native / extern / toolchain / support pack 继续独立；生成器只在构建期运行；Player 闭包不含 Editor、Compiler、Pipeline 或 Toolchain。

## 实施范围与首轮状态（历史）

2026-09-08 用户确认：Editor UI 扩展直接使用 ImGui 是既定设计，保留原生 ImGui flags、UI API 与显式
Editor scripting exports；不再把这些接口列为 Native 封装缺口。这不放宽 BGFX、SDL3、MiniAudio 的后端边界，
也不改变 ImGui DragDrop 的 Identity payload 规则。

本次执行采用两个明确阶段：先完成清单内全部源码、调用方、测试场景定义和 Wiki 实现，再明确报告
“实现阶段完成，尚未验证”，随后集中运行编译、测试、Architecture 与实际工作流验收。
996 项通过仅为上一批基线，不是本次新增实现的验证结果。定时续跑按用户要求保持暂停。

| ID | 收口项 | 完成标准 | 实现 | 验证 |
| --- | --- | --- | --- | --- |
| C01 | 标准与术语 | Overview、项目页、索引与当前事实同步 | 已更新 Overview、专项标准、四个新项目页和相邻 Wiki；未宣称全量历史 Wiki 已重新审计 | 当前非历史页面链接检查通过；历史审计链接另记 |
| C02 | Execution | façade 复用可撤销 scope；异步后代、LIFO、隔离与释放测试 | 已接入 ExecutionSlot / LifetimeScope，删除各领域重复 AsyncLocal owner | 核心 8 项与完整回归通过 |
| C03 | Diagnostics | Core severity/reporter/sink，generation 撤销，删除领域 sink 与 Host 重复转换 | 已统一 Core reporter/hub/sink、领域 severity、Player log 桥接与 reporter ownership | Diagnostics 10 项、Logging 6 项及全量回归通过 |
| C04 | 资源释放 | Shell 异常聚合释放；Asset 无派生 finalizer；owner mutation | Runtime/Play 与通用 Layer/Module/Panel/Registry 退出已统一 Core retirement；Pending 保留 owner，超时封锁依赖释放 | 续轮报告含部分启动、短暂 Pending、终止 timeout 与真实 ALC 验证；领域组合矩阵见 C06 |
| C05 | 不可变发布 | Graph/Mixer/Descriptor/Content 不泄漏可写内部集合 | 编译 Graph/Mixer 等已有冻结副本；追加 Shader/IR/Material 嵌套隔离、Editor Identity scope、Settings contributor 深复制与中立 effective cache；全仓审计未关闭 | Shader 与 Settings 发布负向测试通过；完整矩阵未完成 |
| C06 | Generation | 统一 participant/gate、候选回滚、退休与 GC barrier | Module/Script/Editor、共享 gate、residency 退出已接入；追加完整异常树 Pending 分类、原始错误保留、取消/并发释放与工作同步前缀所有权；Plugin 回滚不提前丢候选 | nested Aggregate / InnerException、相邻普通失败、真实双代 ALC 与 Plugin observer 子项已补实现和回归；执行证据见累积报告；失败启动、剩余 callback 与完整强引用 soak 矩阵仍未完成 |
| C07 | Reference recovery | Assets/Scene/Settings/Graph/Plugins/History 接入同一生产恢复协议 | **部分完成**：Scene + Asset-reference + History、Asset Source Mount 与 Assembly Catalog/Importer 已使用共享候选 Recovery；canonical Identity、metadata、诊断隔离和 last-good 访问已接通；Graph/Settings/Plugin availability 待迁移 | 真实类型 remove/Undo/restore/Redo、Source candidate、后续 participant 拒绝、坏导入、磁盘补偿、诊断回滚、同步操作重入与恢复有生产路径测试；最新结果见累积报告，整项仍未关闭 |
| C08 | Subsystem contracts | Contracts、模板生命周期、DAG、Host/Session 范围，无反向依赖 | Contracts、RuntimeSubsystem、Pipeline、Host/Session、Required/Optional/capability 已实现；统一退休与共享 gate 见 C04 | 启动政策、工厂补偿、Pending/timeout 及整链回归通过，最新数据见续轮报告 |
| C09 | 领域 Runtime | 五领域统一骨架、准确继承、内部职责拆分 | Audio/Render/Input/Storage/Animation 已迁移；Audio owners 已拆分；Rendering 单一 Pipeline generation owner 与持久退休步骤已接入，大型资源 owner 仍待分解 | Audio 与 Rendering 追加 Pending/背压/终止 Fault 测试；整项未关闭 |
| C10 | 内容输入 | Identity 内容 scope、Audio 输入、移除 Rendering.Scene 薄桥 | 已接入 ContentReadScope / SceneContentSource，使用完释放，不持有原对象强引用 | References 6 项与 Editor/Rendering/Audio 回归通过 |
| C11 | Animation binding | target+binding、provider、缺失诊断、generation binding | 已实现显式目标、采样-only、不可变播放副本、TypeCatalog Provider、重复 ID 拒绝与主线程应用 | Animation 6 项通过；未内建任何具体模型 Plugin |
| C12 | 默认装配 | 本地声明、强类型生成器、Default、Host 无重复名单 | 已实现 Runtime.Generators / Engine.Default，Editor/Player 共用装配，无反射中央名单 | 生成器 8 项通过，包括重复 ID、错误泛型/ref/容器与参数名 |
| C13 | Asset/Audio 异步 | 冷路径准备/主线程提交、取消、lease、预算、eviction | Asset 共享 IO/字节预留/主线程物化；租约 Pending 保留且计数只释放一次；Audio cache 用 Core lifetime 先退出 Clip 再释放 Artifact，取消等待者保留到清理完成 | 共享 IO/coalescing、budget eviction 重试、缓存复用拒绝、native no-device 回归通过；其余资源压力与领域失败矩阵未关闭 |
| C14 | 队列与预算 | 明确拒绝/背压，有界排空，不丢关键输入 | Event/Log 有界；Audio collector 整批接纳与统计；无效播放参数在抢占前拒绝、完成背压不重复 Stop/计数；Jobs/所有领域队列未完成统一审计 | Audio 连续 256 帧溢出、重复 identity、部分失败、scope 撤销与 Voice admission 重试通过；其余矩阵未关闭 |
| C15 | 命名与公开边界 | Collections、GameLayerCatalog、GraphicsApi、稳定 ID、最小可见性 | 已迁移相关命名、Importer/Processor 显式稳定 ID、Audio/Render public surface；修正 Editor 私有/公开引用分组 | 编译、脚本测试与符号审计通过；不保留兼容 alias |
| C16 | Solution / Scripting | 真实目录与 Solution 一致；每项目显式脚本导出 | 新项目/移除桥项目/测试分组已同步；Contracts/生成器/backend 不导出为 gameplay API | Solution 与 Architecture 通过，脚本/IDE reference assembly 回归通过 |
| C17 | Architecture | Roslyn public/protected 符号、native、contracts、装配约束 | 已实现编译符号检查、generic/SDL3 修正、Pending 分类规则；ImGui UI 原生公开面按用户设计保留，不迁移 | 既有 8 项源码 + 39 项符号正反例；后续动态 callback 可靠性由 C06/C18 验证，不以静态检查冒充行为证明 |
| C18 | 完整验收 | 编译、全测试、原生、Player、Build、架构与 CI | macOS 本地持续复验；可视内容下的 Editor Play/Reload 重复验收未完成；Windows 原生验收按本次范围排除 | 具体测试数量、二进制与日志以累积续轮报告的最后验收段为准，不沿用旧计数 |

实施顺序：先保存基线，再完成 C02–C05 基础安全，C06–C08 协议，C09–C12 纵向接入，C13–C17 资源与工具，最后 C18。阶段内允许独立改动并行，但每项都必须经真实调用方验证，不能只增加未接入的接口。

## OOP 与依赖约束

`RuntimeSubsystem` 只拥有 Start/Stop、有限帧阶段、异常退出和生命周期资源。`AudioRuntime : RuntimeSubsystem, IAudioService` 组合 IAudioDevice/ClipCache/VoiceScheduler/MixerGeneration；其他领域按相同方式组合自己的算法。不得引入 UniversalEngineSystem 大泛型基类、全局 Service Locator、中央 Plugin 类型名单或把托管 Plugin 放入原生实时 callback。

句柄与对象 Identity 不混用；运行时对象通过 domain-qualified runtime ID 解析，History/持久状态只保存 persistent ID 与稳定语义 ID。Provider/Type/delegate 只能由当前扩展 generation 持有。只读集合必须拥有冻结副本；可编辑资产与运行快照分离。

新引擎能力最少拥有 contract/runtime，本地声明和发行项目引用；Assets/Adapter 项目按真实需要增加。新增能力不得要求修改 Shell、EditorHost、GamePlayerHost、诊断中央 switch 或 reload 中央领域名单。

## 验收矩阵

- 生命周期：失败初始化回滚、部分阶段退出、逆序释放、双 Dispose、停止后访问、Host/Session 隔离。
- Execution：乱序释放、异步后代撤销、跨线程受限访问、旧 scope 不保留 service/ALC。
- Diagnostics：重复 code/target 更新、恢复撤销、generation 退休、旧 reporter 不覆盖新报告、sink 故障隔离。
- Recovery：Missing/null 区分、identity 不变、恢复失败回滚、Undo 栈不移动、恢复后重用原 History。
- Reload：成功与废弃候选退休、uninstall、shutdown、故意保留引用触发 Faulted、Pending 禁止 Play/Build/Export/reload。
- Runtime：DAG/重复声明/缺失依赖/跨 owner 依赖拒绝、设备每帧唯一提交、Edit/Play 共享设备不被误释放。
- 领域：Audio 内容输入/no-device/completion/preload；Animation 双目标不串值/binding missing；Render last-good 与资源 fence。
- Assets：异步冷路径、取消、Artifact 完整性、租约释放、预算与淘汰、只读 Plugin mount 不可修改。
- 装配：增加独立测试 subsystem 只修改其声明与项目引用；脚本编译与 IDE 使用同一裁剪引用。
- 发布：macOS ARM64 本地验证；Windows x64 CI；native release Support Pack；Player 不包含 authoring 闭包。

## 首轮验证记录（历史）

**以下是首轮结束时的状态，不是当前结论。** 当时实现了主干与多项真实行为，但整个 C01–C18 清单没有全部关闭；后续实现与验收见页首当前状态入口。不以测试全绿替代缺失功能的实现。

- `/tmp/inno-consolidation-current-build.log`：迁移 Subsystem 与内容协议后，Solution build 通过，0 warning / 0 error。
- `/tmp/inno-generation-build.log`：统一 generation coordinator 首次接入后，Solution build 通过，0 warning / 0 error。
- `/tmp/inno-consolidation-tests.log`：首轮完整测试发现 5 个本轮迁移回归（Assets/Settings 文案 2 个、音频脚本句柄 1 个、动画解码 2 个）。已定位并修正，不作为“基线失败”豁免。
- `/tmp/inno-generation-tests.log`：脚本/Plugin 集成测试 57 项通过，包括成功替换、失败候选、Plugin Missing 与恢复路径。
- 执行测试需要 VSTest 的本地通信端口；沙箱首次拒绝后，通过权限流程在沙箱外运行。不存在绕过测试或改用测试专用访问后门。
- `/tmp/inno-consolidation-architecture.log`：首次收口审计仍有 XML 风格和新 Contracts 项目分类等违规，尚未宣布 Architecture 验收通过。
- Windows x64 仍需 CI 实际执行；不能把 CI 配置存在当作验证通过。

### 最新本地证据

- `/tmp/inno-consolidation-build-7.log`：完整 Solution build 成功，0 warning / 0 error。使用单节点、禁用共享编译器；本地 restore 关闭 NuGet 在线漏洞查询以避免环境 CookieContainer 故障，不代表执行了漏洞审计。
- `/tmp/inno-consolidation-tests-4.log`：47 个测试项目，714 passed / 0 failed / 0 skipped；涵盖 Build、原生 binding/adapter、Audio、Animation、Runtime、Editor、Scripting、Plugins 和 Core。
- 过程中检出的回归均已修正：旧 Importer CLR 名称测试数据、Asset 冷载触发 Authoring refresh、清理失败测试仍期待静默成功、Build read lease 与自动 Catalog refresh 冲突。
- `/tmp/inno-consolidation-support-pack-2.log`：曾成功生成 macOS ARM64 Release Support Pack；随后为最新 gate 修正重新生成，不能以旧包代替最终 Player E2E。
- 第一次实际发布发现 Support Pack 工具仍引用迁移前 Player 路径，已改为 `src/composition/player/Inno.Player`。
- 本次未修改任何玩法 Plugin，也未重写第三方 native binding；使用现有固定 native 产物进行加载、no-device 与发布验证。

### 最终发布验证

- `/tmp/inno-consolidation-audit-final.log`：Architecture validation passed，包含真实目录/Solution、引用图和 Roslyn public/protected 符号检查。
- `/tmp/inno-consolidation-support-pack-3.log`：最新 macOS ARM64 Release Support Pack 生成成功，位于 `/private/tmp/inno-consolidation-validation.AmIEJP/support/macos-arm64`。
- `/tmp/inno-consolidation-player-e2e-final.log`：实际导出、启动 Metal Player 并完成三帧 smoke；进程成功退出，Player E2E passed。部署目录为 `/private/tmp/inno-consolidation-validation.AmIEJP/player-final/Builds/InnoPlayerE2E.app`。
- E2E 检查 runtime-only managed closure、无 Audio/Animation authoring importer、恰好一份 MiniAudio Release 动态库。生成器未进入 Player runtime 闭包。
- `git diff --check` 通过。当前非历史 Wiki 的本地文件链接无缺失；历史 2026-08-31 审计仍有旧路径/行号链接，未冒充当前源码签名或改写历史证据。
- 本机运行不等于 Windows x64 已验收；未运行远端 CI，没有提交或推送。
- 本次 Player smoke 为无玩法模型的三帧生命周期验证（views/draws/dispatches 均为 0），不代表视觉输出或可听声音效果已验收。
- 最终提示音已尝试播放，环境返回 `AudioQueueStart failed (-66680)`，未能实际播放。

## 已实现功能总结

| 方面 | 实际变化 |
| --- | --- |
| OOP 骨架 | 领域 Runtime 直接派生 RuntimeSubsystem、实现领域 service，并组合 backend；删除只做转发的领域 runtime feature 包装；Core Layer/LayerStack 不承担子系统代际 |
| 诊断 | 一个 Core DiagnosticSeverity / DiagnosticHub；Reporter 生产、Sink 消费；generation 退休撤销 reporter，Player 统一桥接日志 |
| 执行上下文 | 一个 ExecutionSlot / LifetimeScope 协议；领域 façade 仍保持简单，基础设施显式持有 service，scope 严格 LIFO 且能撤销异步后代的强引用 |
| 装配 | 四个新增本体项目 Core.Execution、Runtime.Contracts、Runtime.Generators、Engine.Default；本地 attribute 生成静态 factory catalog，Host 不复制领域名单 |
| 音频 | 服务 Voice 与原生 Voice 身份分离；MiniAudio 真正异步准备；Preparing/Ready/Failed、preload 取消、Artifact lease、版本化内容请求与 emitter 同步 |
| 资产 | AssetRuntimeOwner 限制状态写入；ArtifactRetention 进入回收可达集；Database 接收固定 TypeCacheSnapshot，冷 IO 与 owner 物化分离 |
| 动画 | 显式 AnimationTarget，target+binding 独立混合；播放冻结创作数据；Provider 自动发现、重复 ID 校验、缺失/异常诊断；无内建玩法绑定 |
| 引用输入 | Audio/Rendering 共用 Identity-backed ContentReadScope；SceneContentSource 负责投影；使用后释放，不维护各自 managed object 表 |
| 代际 | Module/Script/Editor 共用 coordinator；Build/Export 读租约；自动刷新预留互斥并延后；退休清理错误不再吞掉，Fault 后要求重启 |
| 规范 | 稳定 Importer/Processor ID、Collections/GameLayerCatalog/GraphicsApi 命名；公开依赖分组；Scripting 本地显式导出；Solution/真实目录与 CI 同步 |

### 关键源码结构

```text
src/
├── foundation/core/Inno.Core.Execution/
├── foundation/core/Inno.Core.Diagnostics/
├── foundation/extensibility/Inno.Extensibility.Reload/
├── content/
│   ├── references/Inno.References/
│   ├── assets/Inno.Assets/Runtime/
│   ├── scene/Inno.Scene/
│   └── animation/{Inno.Animation,Inno.Animation.Runtime,Inno.Animation.Assets}/
├── services/
│   ├── audio/{Inno.Audio,Inno.Audio.Runtime,Inno.Audio.Assets}/
│   ├── rendering/{Inno.Rendering,Inno.Rendering.Runtime,...}/
│   └── {input,storage,platform}/
├── runtime/
│   ├── contracts/Inno.Runtime.Contracts/
│   ├── engine/Inno.Runtime/Subsystems/
│   ├── generators/Inno.Runtime.Generators/
│   └── {scripting,plugins}/
├── adapters/
│   ├── default/
│   └── {audio,rendering,input,storage,platform,presentation}/
└── composition/
    ├── default/Inno.Engine.Default/
    ├── shell/Inno.Shell/
    ├── player/Inno.Player/
    └── editor/{host,framework,features,presentation,panels}/
```

上树只列本轮重点，不替代 Overview 中的完整项目归属。native、extern、build、tests、tools、docs 仍保持各自顶层角色。

## 历史剩余清单（已由 2026-09-08 实现记录逐项承接）

1. **C07 必须完成生产迁移，不能只保留通用工具类。** 以 Assets/Scene/Graph/Settings/Plugin/History 的真实 missing slots 建立统一候选参与者；领域保存自己的中立状态，Host bridge 负责参与共享 recovery，避免 Foundation Settings 反向引用 Content。要求同一恢复批次失败原子回滚，Undo/Redo barrier 保留并自动恢复。
2. **C04/C08 需要完成 pending retirement 协议。** LifetimeScope 已拒绝带未完成任务的同步 Dispose，但 Pipeline/Session/Host 还没有完整的“取消→owner-thread 排空→确认 quiescent→释放依赖”链。不能在依赖仍被任务使用时仅聚合异常后继续销毁它们。
3. **C05/C09 需要完成领域内部 owner 和快照审计。** 冻结编译结果与可编辑创作对象不能混为一谈；继续拆 Audio Cache/Voice/Mixer/Recovery 与 Rendering 资源负责人，并覆盖故障时全部租约释放。仅拆 partial 文件不算职责拆分。
4. **C13/C14/C17 的压力与负向矩阵未完成。** 还包括 IO 峰值 bytes、并发合并、Job/领域队列、静态/event/task/native callback 泄漏 soak，以及架构规则的专用坏例 fixture。
5. Windows x64 必须在 CI 实机运行。macOS 测试或 CI YAML 存在均不能替代这个验收。

上述项未关闭前，不应把本次交付描述为“所有功能均已实现”或“本体已经彻底收口，可以保证任意 Plugin 热重载安全”。

## 2026-09-07 后续修复：空项目启动与 Rendering2D 当前项目同步

本次范围仅为用户报告的空目录启动错误、Rendering2D 对当前引擎的适配，以及真实验证暴露的程序集退休引用环，不代表 C01–C18 的所有剩余工作已完成。

### 实际改动

- Editor authoring 与 Build CLI 从 `DirectoryInfo.Name` 派生初始 Project ID。末尾分隔符不再使 `Path.GetFileName` 返回空名称；绝对目录和相对 `./` 均可打开空项目。没有放宽 `ProjectId.FromName` 的参数校验。
- Rendering2D Pipeline 改用当前 `InnoEngine.Diagnostics.Diagnostic` / `DiagnosticSeverity` 与上下文 reporter，没有恢复旧 Rendering 诊断类型。
- 当前 AssetPipeline 重新导入创作源，仅 README 的 `.imeta` 需要把 Importer ID 更新为 `inno.assets.text`。全部 Asset persistent ID 保留；该 metadata 的 importer settings、persistent ID 和 source kind 与原数据逐字节相同。
- Rendering2D 使用当前 Settings writer 显式保存既有 `rendering2d` Project/Plugin ID，避免目录派生 ID 改变安装源身份；导出默认路径统一为 `Builds/rendering2d.iplugin`，其他设置保留。无效的 `SampleScene` Player 入口清空，实际 `Assets/~Samples/SampleScene.iscene` 保持 Editor/Play 示例用途，不绕过 Sample 的 Player 排除规则。
- 更新 Rendering2D 启动命令、示例名称、导出说明和 Sample 边界；重新生成 IDE 引用与安装包。不手改安装包、只读解包缓存，不增加 alias、migration 或旧格式读取分支。
- 真实 Rendering2D 退出曾触发严格 GC barrier 的 30 秒异常。堆快照显示下游 `ModuleLoadContext` 的共享 Assembly 解析表与跨上下文 loader allocator 依赖构成保留环；现在 `Unloading` 会释放该解析表，覆盖正常退休和加载失败路径。仍必须通过原有 GC/weak monitor，未调整 timeout 或 Faulted 行为。修复后 180 帧运行正常，退休约 0.43 秒。

### 验证证据与限制

- `/tmp/inno-empty-project-smoke.log`、`/tmp/inno-empty-project-relative-smoke.log`：实际 TestProject 使用绝对末尾 `/` 和相对 `./` 分别运行 10 帧、正常退出；`Assets` / `Plugins` 仍为空，没有安装测试包。
- `/tmp/inno-rendering2d-ide-build.log`：Rendering2D Runtime/Editor IDE 脚本项目均编译通过，0 warning / 0 error。
- `/tmp/inno-rendering2d-project-smoke-fixed.log`：Rendering2D 源项目实际运行 180 帧并正常退出。
- `/tmp/inno-rendering2d-plugin-final.log`：当前 Build Pipeline 原子导出成功；manifest 的 Plugin ID 是 `rendering2d`，包内 README Importer ID 是 `inno.assets.text`。
- `/tmp/inno-rendering2d-final-installed-smoke.log`：最终包在全新临时项目安装、激活 Runtime/Editor Plugin 程序集、运行 180 帧并正常退出；没有只验证 ZIP 文件存在。
- `/tmp/inno-project-module-tests.log`、`/tmp/inno-project-scripting-tests.log`、`/tmp/inno-project-build-tests.log`：既有 Modules 13 项、Scripting 57 项、Build 23 项全部通过。本次没有修改 tests 源码。
- `/tmp/inno-project-final-solution-build.log`：完整 Solution build 通过，0 warning / 0 error。`/tmp/inno-project-architecture.log`：Architecture 通过。
- `/tmp/inno-project-full-tests.log`：47 个测试项目，**713 passed / 1 failed / 0 skipped**，不能标为全绿。失败为 `EditorHistoryTests.CompensationFailureFaultsHistoryAndRejectsFurtherTransactions`，发生在 fixture 启动而非 History compensation 本身：测试 Module 的首次 `OnStart` 重入 `TypeCatalog.Rebuild()`，被处于 Transitioning 的 generation gate 拒绝。
- `/tmp/inno-project-history-baseline.log`：通过临时 MSBuild 编译输入恢复本轮修改前的 `ModuleLoadContext`，同一测试独立运行仍产生相同调用链和异常，证明不是本轮卸载解析表清理引入。该 reentrant catalog refresh 缺口仍需单独处理；未修改测试、绕过 gate 或把失败静默跳过。对照不改引擎源码，随后恢复正常 Solution 构建产物。
- `/tmp/inno-project-restored-modules-build.log` 与最终 `/tmp/inno-project-delivery-build.log` 记录对照后的正常源码构建，最终完整 Solution 为 0 warning / 0 error；`/tmp/inno-project-delivery-architecture.log` 再次通过架构检查。`/tmp/inno-rendering2d-delivery-smoke.log` 记录最终产物运行 Rendering2D 源项目 30 帧并正常退出，确保未留下诊断用的旧实现产物。
- 没有宣称完成 Windows CI、实际 Player 发布、Play 切换专项或完整跨域 Recovery 验收。前述只读 metadata 不一致时失败记录再次抛异常的通用问题也未在本次添加兼容处理；当前 Rendering2D 包通过更新创作源解决不一致。

本次诊断堆仅在本机临时目录分析，完成定位后已删除；创作源/设置备份仍在 `/tmp/inno-rendering2d-authoring-before-update.tgz`。没有删除资产、重置用户工作区或提交 Git。
