# 本体收口累积验收：2026-09-07—09-08

[架构索引](README.md) · [前轮报告](ENGINE_CLOSURE_ACCEPTANCE_2026_09_07.md) · [完整实施清单](ENGINE_CONSOLIDATION_IMPLEMENTATION.md) · [Overview](ENGINE_ARCHITECTURE_OVERVIEW.md)

**本页为之前的累积证据；当前状态请看 [2026-09-08 实现交付与集中验收](ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md)。本页历史“尚未完成”不是新的工作清单。自动续跑已按用户要求暂停。**

## 当时的结论与范围

本轮继续实现前轮的剩余项，没有开发玩法 Plugin，也没有修改 TestProject 或 Rendering2D 创作源。
**Subsystem descriptor、通用扩展退休、共享资产 IO 与 Scene/Asset-reference/History Recovery 的纵向子项已实现，但整体仍未全部收口。**
尤其 C07 仍不能关闭：本轮没有将 Assets/Scene/Graph/Settings/Plugins/History 全部迁移到统一 Missing-slot Recovery。
跨平台原生验收仍按用户要求排除；普通单测中的 Windows 打包数据验证不等于 Windows 上运行。

最新继续基线：**996 passed / 0 failed / 0 skipped，48 个测试项目**；本页末尾记录 C06 嵌套退休异常、
取消/并发释放、任务前缀所有权与 Plugin 候选回滚的完整证据及仍未完成的工作。此数字不是全项目收口完成声明。

## 通用扩展生命周期追加收口（2026-09-08）

本次继续请求完成了上一轮明确留下的 **Layer / Editor Module / Panel / Registry 通用退出链**，
并追通到 TypeCatalog、ModuleHost、GenerationCoordinator、RuntimeSession、EngineHost。不是再添加一个只针对 Play 的特殊分支。

| 子项 | 当前实现 |
| --- | --- |
| 部分 Attach/Start | Layer、Module、Panel 在调用 hook 前即登记所有权；失败也执行 Detach/Stop 补偿，未成功启动者不能跑帧 |
| Stack 退出 | 当前 Pending entry 不移除，EventHub 与下层 Layer 保留；订阅在退出开始时撤销，禁止再订阅；完成的 entry 不重复清理 |
| Editor 生命周期 | 移除 TryStop/TryDetach 的日志化 false；普通退出和恢复失败聚合上抛并 Fault；Panel 只有补偿成功才能被隔离 |
| 共享退出机制 | TypeRegistry 使用 Core LifetimeScope 逆序去重退休；单个 Stop/Detach 通过 Core RetirementBarrier 协作式、有界排空 |
| 退出阶段所有权 | EditorInteractionRuntime 保存 shutdown stage/action/snapshot，Pending 不标 disposed；ImGui、EditorLayer、资源栈逐层保留同一未完成 owner |
| Type/Assembly 事务 | Pending 不被聚合为普通错误后继续；Prepare/Activate/Complete/Rollback 保留未完成事务，不启动仍在使用的旧代或候选 ALC Unload |
| 跨 owner 依赖保护 | GenerationCoordinator.EnsureRetirementSafe 是公共基础设施校验；Registry、TypeCatalog、ModuleHost、Session、Host 共用。即使诊断里先记录另一条普通错误，也不会掩盖 Pending |
| 终止性 timeout | deadline 失败后重复 Dispose 也不能新开一个 deadline、清空字段或释放下层依赖；不支持在同一 Host 内 reset/retry-generation。必须重启进程 |

公开面只增加必要的基础设施边界：

- `TypeRegistry(TypeCatalog, TimeSpan? retirementTimeout = null)`：派生 Registry 可设置正值的协作式退休 deadline，默认 30 秒。
- protected `RetireResource(string owner, Action retire)`：在控制线程处理 Stop/Detach，正常返回后不保存 callback；不是实时音频/DSP 回调接口。
- `DisposeExtensions` 从 protected static 改为实例成员，因为预算、Fault 与未完成资源所有权属于 Registry；全部生产调用方已同步，不保留兼容 overload。
- `GenerationCoordinator.EnsureRetirementSafe()`：与阻止新代际的 EnsureReady 分工不同，只判断是否还有禁止销毁依赖的未退休工作。

没有新建 LifecycleManager、Editor 专属 Pending 协议、第二个 diagnostic sink、backend switch 或测试后门。
超时保留的强引用属于 Faulted Host 的未完成退休事务，绝不写入 Missing、History、Settings 或后续 generation。
deadline 是协作式协议：hook 必须及时返回 Pending，不能在 hook 内无限阻塞并期待引擎安全抢占任意托管代码。

本次纠正了上一轮一个较弱行为：启动退休超时后，后续 Host.Dispose 可能因工作恰好结束而继续释放其他资源。
现在共享的未退休屏障保持终止性，相关公开契约测试改为断言依赖仍被保留，而不是放宽错误或跳过测试。

新增/加强回归覆盖：Core Layers 4 项、Types Registry 3 项、Editor lifecycle 5 项、GenerationCoordinator 4 项、
真实 Modules ALC 3 项，另加强原 Runtime timeout 测试。实际 ALC 测试覆盖 Activate/Complete/Rollback 三条路径，
直接观察两代 context 的 Unloading 事件均未提前触发，并验证重复 Dispose 仍阻塞。

首轮 48 项目 781 项通过后继续增加上述真实 ALC / shared gate 验证；最终结果见本页末尾追加验收，
前轮 766 项记录保留为历史证据，不当作本次最终二进制结果。

## Recovery 生产迁移追加（2026-09-08）

已把 `ReferenceRecoveryTransaction` 改为共享 `IGenerationChange` 五阶段事务并接入 SceneReloadService，
不再只有测试调用。捕获真实 Component/System 与 Asset dependency slots；对象 persistent ID 和逻辑 type ID
分开使用。候选对象注册后统一解析/Validate，结构回滚与旧 publication/属性恢复明确分阶段。

同时修复生产 History 缺口：原先类型 Missing 时 Query 禁止 Undo，RestoreComponent/System 也拒绝创建占位。
现在使用 SceneElementSerialization 的中立 element state，保留类型名、属性、依赖、引用别名；
删除/移动 Missing 时记录原逻辑类型，Undo 恢复占位，类型返回后原 Redo 可用。普通单属性 History 没有改为整 Scene 快照。

EditorReloadCoordinator 将 Assembly 与外部 Asset/Settings 作为同一 publication 边界：先恢复旧结构，
再恢复旧 Type/Serializer/外部 resolver，最后恢复领域属性。修复了外部 resolver 原先在 Scene 属性之后才恢复的顺序问题。
Missing 恢复不再接受 ignored/failed properties；失败撤销候选并保留原占位；提交后退休失败仍必须 Fault。

C07 **仍未整体关闭**：Assets 全量 importer/canonical candidate、Graph、Settings、Plugin availability 尚未全部进入
同一个生产 Recovery 批次。当前已完成的是 Scene + Asset 引用 + History 的真实纵向链，不是对所有领域的空槽位包装。
Foundation Settings/Graphs 仍不能反向依赖 Content.References；后续由上层 owner 组合，不把 semantic ID 伪装为 object Identity。

## 本轮实际关闭的子项

| 缺口 | 实现与所有权 | 可执行验证 |
| --- | --- | --- |
| Pipeline 构造失败丢失 owner | Pipeline 构造器变为 internal 且不执行 factory；Host/Session 先登记字段/集合，再 Start。失败 startup 在同一 owner 下排空；超时保留 owner 并 Fault | Host/Session × Factory/Attach 四组合故障注入；资源恰好释放一次；补偿后可再启动 |
| 退休重试丢失此前错误 | LifetimeScope、RuntimeSubsystem、Pipeline、Session、Host 保留跨 Pending 的清理失败；已退休 entry 移除，不重复 Dispose | 先故障、后 Pending、再完成的逆序回归；OnStop Pending 不提前释放工厂资源 |
| 无界/重置的退休 deadline | Core `RetirementBarrier` 在 owner thread 推进；deadline 不随重试重置。`RetirementTimeoutException` 仍属于未退休信号，不能释放依赖 | 超时终态、重复调用、真实失败 Host Pipeline 仍受拥有、gate Fault 后拒绝新操作 |
| Play entry/exit 丢失 Session/History | 资源获得后即时放入 Controller 字段；Pending 不清空字段、不标 disposed；Scene entry 失败共用 Stopping 补偿；History 最后释放 | 真实 Controller/Session 的成功退出与 Scene entry 失败两种 Pending 场景；禁止新 generation；排空后恢复 History |
| Editor 最外层先拆模块再停 Play | `EditorPlayModeLoop.Quiesce` 由 EditorHost 在资源栈前调用；Module Stop 和 Reload 复用 Controller quiescence | Play Mode 和真实 collectible Plugin reload 专项包含在全量测试；本轮不据此声称有可视内容的 UI E2E 全完成 |
| Optional/Required/capability 缺失 | Descriptor 冻结 requirement/requiredCapabilities；Factory 使用独立 construction LifetimeScope。Optional 仅在完整补偿后成为 Unavailable；必需消费者不可用则拒绝整个 owner | 10 个新增 policy 测试：能力、冻结、失败、依赖传播、cycle、清理失败、timeout |
| Editor extension Dispose 只记日志 | EditorExtensionCatalog 使用 TypeRegistry 的统一去重逆序 DisposeExtensions；尝试全部后聚合，并 Fault 共享 gate | 原有注入 Dispose 故障的测试加强为验证抛出 + Fault，同时保留全部顺序/次数断言 |
| 不同冷加载 root 重复读取同一依赖 | Payload 任务按不可变 identity/path/length/hash 共享；root 和 caller 分别计数；unique bytes 仅预留一次；每数据库最多 4 个文件 IO | 两种取消策略各 32 轮（共 64 轮）真实部署 Catalog/Artifact 读取；3 个唯一读取服务两个 root 与同一依赖 |
| 资产队列缺观测 | `AssetPreparationStatistics` 暴露 current/peak/rejected/unique-read/shared-read/bytes，纯中立值，无 Asset/Task/Type 根 | 32 请求容量拒绝、Dispose 取消全部 waiter、损坏共同依赖使两 root 均失败且不发布资产，全部预留归零 |

## 代码结构与公开面

```text
src/foundation/core/Inno.Core.Execution/
  LifetimeScope.cs                 resource/work ownership; pending failure retention
  RetirementBarrier.cs            bounded owner-thread drain, no resource ownership
  RetirementPendingException.cs   dependencies are still live
  RetirementTimeoutException.cs   deadline failed; dependencies remain live

src/runtime/contracts/Inno.Runtime.Contracts/
  RuntimeSubsystemDescriptor.cs   immutable ordering + requirement + capability prerequisites
  RuntimeSubsystemRequirement.cs  Required / Optional startup policy
  RuntimeCapabilityId.cs          open, backend-neutral semantic ID
  RuntimeSubsystemContext.cs      foundation services + private factory lifetime + capability snapshot

src/runtime/engine/Inno.Runtime/
  Hosting/EngineHost.cs            registers owners before startup; faults failed retirement
  Hosting/RuntimeSession.cs        owns partial initialization and frame-safe disposal
  Subsystems/RuntimeSubsystemPipeline.cs
                                  ordering, per-factory ownership, unavailable diagnostics, retirement

src/content/assets/Inno.Assets/Runtime/
  AssetDatabase.Residency.cs       shared immutable IO, root/caller ownership, admission accounting
  AssetPreparationStatistics.cs   immutable observability snapshot
```

没有新建第二套 Lifecycle Manager、Diagnostic Sink 或 backend switch。`RuntimeCapabilityId` 不替代设备的领域能力集合，
而是 Composition 检查设备/服务后，向通用 Runtime 提供的启动前提；它不是 object Identity，也不能用作对象索引。
Subsystem 契约仍不进入 gameplay scripting whitelist，游戏使用 Audio/Input/Storage 等中立 façade。

新增公开入口的必要性：

- `RetirementBarrier.TryComplete/Wait`：产品、运行时共享同一种 deadline 与 Pending 语义；不保存 callback，不把资源提升到全局容器。
- `EngineHostBuilder.UseRetirementTimeout`：真实 Host 可配置故障排空的正值 deadline；不是迁移或静默 fallback 开关。
- `RuntimeSubsystemRequirement` / `requiredCapabilities` / `RuntimeSessionOptions.capabilities` / `CreateHostPipeline(..., capabilities)`：相同启动政策实际用于两种 owner。
- `RuntimeSubsystemPipeline.startupDiagnostics`：可观察 Optional 不可用，同时复用 Core DiagnosticHub；只有中立数据。
- `EditorPlayModeLoop.Quiesce`：最外层产品在拆卸依赖前排空 Play，不暴露 Controller 内部或具体 backend。
- `AssetDatabase.preparationStatistics`：验证合并/容量/回收，不借助反射、IVT 或测试专用 IO 后门。

线程边界：只有 metadata 的 EngineHost 允许在异步 Build 后退出，因此它在首次退休时建立 barrier；
实际附着的 RuntimeSubsystem 仍严格检查自己的 control thread。不能为修 Build 测试而放开音频、图形或 Session 的线程约束。

## 验收证据

| 验证 | 结果与日志 |
| --- | --- |
| 全量测试 | `/tmp/inno-closure-next-tests-verified.log`：最终二进制 48 项目，766 passed / 0 failed / 0 skipped；退出码 0 |
| Solution build | `/tmp/inno-closure-next-build-verified.log`：0 warning / 0 error；退出码 0 |
| Architecture | `/tmp/inno-closure-next-architecture-final.log`：validation passed |
| Runtime 专项 | `/tmp/inno-closure-next-runtime-3.log`：26 passed |
| Play Mode 专项 | `/tmp/inno-closure-next-playmode-1.log`：21 passed |
| Assets 冷 IO / importer 专项 | `/tmp/inno-closure-next-assets-final.log`：56 passed，含共享损坏输入与 IO gate 退出；同样包含在最终全量结果 |

首轮全量测试实际发现 Host barrier 过早绑定创建线程，以及旧测试仍要求吞掉扩展 Dispose 故障。
前者修正生产线程边界；后者加强真实公开错误契约和 gate 断言，没有删除/跳过测试，也未作为基线失败豁免。

## 仍未关闭的整项

| 项 | 当前必须继续完成的工作 |
| --- | --- |
| C07 | Scene + Asset-reference + History 已接入生产 Recovery；继续完成 Assets 全量候选、Graph、Settings、Plugin availability 的统一批次和跨域失败矩阵 |
| C05 | Shader/IR/Material 嵌套声明已隔离，Scene presentation 已复用 Identity scope；继续审计其余发布面与负向矩阵，不能只看只读集合 |
| C09 | Audio Voice/content/candidate owner 已实际拆分；Rendering 大型资源 owner 的分解仍未完成 |
| C06 | 通用 Layer/Module/Panel/Registry 与真实 Module ALC 的退出故障已补；所有领域自定义工厂、真实设备 callback/Task 及跨域恢复的完整组合矩阵仍未关闭 |
| C13/C14 | 不同 root 共享依赖与资产统计子项已完成；其他 Job/Audio/Render/retirement 队列的完整指标、长期大负载和剩余容器 admission 审计仍未完成 |
| C17 | 完整符号、ImGui payload、动态 callback 的架构负向矩阵 |
| C18 本地 | 真实可视内容下 Editor Play → Stop → Reload → Play 的重复纵向验收；空项目 smoke 不替代它 |
| C01 | 全量历史 Wiki 示例/签名/状态审计；本轮只更新实际变更模块及架构入口 |

“Subsystem descriptor”不再单列为缺实现的阻断项。Optional 的政策边界已明确为初次启动；运行帧失败仍传播给产品，
没有声称提供动态自动降级/重启。旧报告保留历史事实，以本页为最新状态，不将规划批量勾选成完成。

## 最终产物复验

- `/tmp/inno-closure-next-support.log`：最终 macOS ARM64 Release Support Pack 成功，退出码 0，产物位于 `/tmp/inno-closure-next-validation.MLhQFq/support/macos-arm64`。
- 包内 miniaudio 原生文件恰好为 `native/miniaudio/macos-arm64/libminiaudio-release.dylib`，无 Debug miniaudio 混入。
- `/tmp/inno-closure-next-player-final.log`：使用重建后的 E2E 校验程序导出并启动 Player；Metal/BGFX 初始化、运行、退出完成，`Player E2E passed`，整个校验进程退出码 0。产物 `/tmp/inno-closure-next-validation.MLhQFq/player-final/Builds/InnoPlayerE2E.app`。
- 首次 Player 尝试的校验程序仍带旧 Debug Runtime DLL，虽然被测应用成功，但校验程序自身退出异常；该次不计通过。全 Solution 重建、DLL 同步后重新生成并验证上述 `player-final`。
- `/tmp/inno-closure-next-editor-final.log`：最终 Debug Editor 在 `/tmp/inno-closure-next-validation.MLhQFq/EmptyProject/` 运行 30 帧，保存状态，完成 Dispose/BGFX shutdown，退出码 0。没有修改用户 TestProject。
- Architecture 在最终构建后再次通过；`git diff --check` 通过；本轮涉及的 16 个 Wiki/架构页面、131 个本地链接检查全部有效。
- 空项目 smoke 不证明带可视内容的 Play/Reload E2E 或真实音频可听，相关完整验收仍保留在未完成表。
- 完成提示音 `afplay /System/Library/Sounds/Glass.aiff` 执行成功，退出码 0；不将系统提示音当作游戏 Audio 功能验收。

## 2026-09-08 最终追加验收

| 验证 | 最终结果 |
| --- | --- |
| Solution build | `/tmp/inno-lifecycle-closure-build-final.log`：0 warning / 0 error，退出码 0 |
| 全量测试 | `/tmp/inno-lifecycle-closure-tests-verified.log`：48 项目、785 passed / 0 failed / 0 skipped；最终构建二进制 no-build 复验，退出码 0 |
| Architecture | `/tmp/inno-lifecycle-architecture.log`：validation passed；包含公开边界、依赖、Solution Folder 和源码规则 |
| 真实 collectible ALC | `/tmp/inno-module-retirement-tests.log`：Modules 项目 23 项通过，包含新增 3 种事务阶段 Pending；退出码 0 |
| Runtime timeout | `/tmp/inno-runtime-retirement-verified.log`：Runtime 项目 26 项通过；聚合诊断不能掩盖未退休状态 |
| macOS ARM64 Support Pack | `/tmp/inno-lifecycle-validation.TJ63EG/support/macos-arm64` 重建成功；miniaudio 仅有 Release dylib |
| Player E2E | `/tmp/inno-lifecycle-player.log`：使用本轮 Support Pack 导出、运行并正常退出；整个校验程序退出码 0 |
| Editor 空项目 | `/tmp/inno-lifecycle-editor.log`：新临时项目运行 30 帧，保存状态，完成 Dispose 与 BGFX shutdown，退出码 0 |

Player 产物位于 `/tmp/inno-lifecycle-validation.TJ63EG/player/Builds/InnoPlayerE2E.app`；
Editor 临时项目位于 `/tmp/inno-lifecycle-validation.TJ63EG/EmptyProject`。这些验证没有修改用户创作项目。

本次新增 19 个测试用例，并加强现有 timeout/History 夹具观察契约，没有新增 known failure、跳过或基线豁免。
出现过的中间失败均已处理：History 观察夹具不应在已 Faulted History 上创建新事务；共享 gate 必须识别
当前 startup 聚合诊断里的 Pending，而不是只检查异常最外层。上表仅记录修复后最终运行结果。

本次关闭的是 C04/C08 的通用生命周期子项，不是全部本体收口。C07、C05、C09、C06 剩余组合覆盖、
C13/C14、C17、C18 可视内容纵向验收和 C01 全 Wiki 审计继续按未关闭表推进；跨平台原生验收仍排除。

最终文档检查：本轮涉及的 14 个 Wiki/架构页面、75 个本地链接全部有效；`git diff --check` 通过。
Architecture 再次复验通过，日志 `/tmp/inno-lifecycle-architecture-final.log`。
完成提示音 `/System/Library/Sounds/Glass.aiff` 已播放，`afplay` 退出码 0。

## 2026-09-08 Recovery 生产链验收

本次新增 15 个测试，最终基线从 785 增加至 800。新增的场景是生产公开入口和真实 collectible Plugin 测试模块，
没有 IVT、私有反射、测试专用生产接口或跳过失败。

| 子项 | 本次结果 |
| --- | --- |
| 共享 Recovery | 五阶段、真实 slots、重复 key、错误阶段、跨线程、Prepare/Apply/Validate 失败补偿、Pending 阻断、普通退休失败聚合 |
| Scene 生产调用 | SceneReloadService → SceneReloadRecovery → ReferenceRecoveryTransaction → Scene domain participant；不是空槽位占位调用 |
| Missing Element History | Component/System 在类型缺失时 Undo 重建；移动/删除占位保存原逻辑类型和全部 bytes/依赖/aliases |
| Asset 引用纵向链 | 删除 Component → 移除 Plugin 类型与 Asset → Undo Missing → 同一 .imeta 身份恢复 Asset/类型 → Redo 删除同一逻辑对象 |
| 失败候选 | restore callback 拒绝时保留原占位与历史分支，candidate ALC 经完整 GC barrier 后回收；可继续原 Undo/Redo |
| 文档状态 | Missing 元素移动/删除/Undo 后可保存；自动类型恢复和再次 Missing 不使已保存 Scene 变脏 |
| 外部 publication 顺序 | 旧结构 → 旧 Assembly/Type/Serializer 与外部 Asset resolver → 旧领域属性；不会使用候选 resolver 恢复旧状态 |

新增/修改公开面及必要性：

- `ReferenceRecoveryTransaction : IGenerationChange`、`IReferenceRecoveryParticipant : IGenerationChange`：复用唯一五阶段协议。
  删除独立 Activate/IDisposable/Rollback 入口，不留旧 overload；事务不能越过 publication 自行恢复属性。
- `ISceneReloadStateTransfer.recoveryChanges`：供 Host 诊断和真实 API 测试读取本批次槽位；只有中立描述与当前 runtime identity 值，
  不保留旧 instance/Type，不能进入持久化。
- `SceneElementSerialization.CaptureState/RestoreState`：统一 Scene 元素与 Missing 中立数据；RestoreComponent/System 的 stateData
  使用该封套而非普通 property-data。内部 SceneElementState 通过 Core Serialization 编码，不是新的 persistence side channel。
- 所有新增恢复入口是基础设施边界，不加入 gameplay Scripting API；SceneEdits 仍是用户数据修改的统一入口。

代码组织：

```text
src/content/references/Inno.References/
  ReferenceRecoveryTransaction.cs       shared generation phases and owner retirement
  IReferenceRecoveryParticipant.cs      domain application plus candidate slot validation

src/content/scene/Inno.Scene/
  Reloading/SceneReloadRecovery.cs       composition of shared recovery and Scene owner
  Reloading/SceneElementReferenceResolver.cs
                                        Identity-based candidate element lookup
  Serialization/SceneReloadStateTransfer.cs
                                        existing domain structure/property compensation
  Serialization/SceneElementSerialization.cs
  Serialization/SceneElementState.cs    neutral element capture and Missing restoration

src/composition/editor/
  framework/Inno.Editor.Core/Reloading/EditorReloadCoordinator.cs
                                        combined Assembly/external publication boundary
  features/Inno.Editor.Scene/Documents/SceneEdits.cs
  features/Inno.Editor.Scene/History/SceneElementHistoryHandler.cs
                                        same neutral state, persistent identities and History stack
```

本次没有新增项目、概念层、真实 gameplay Plugin 或 compatibility reader。Native/Adapter/Shell 边界不变。
原有普通单属性 History 仍只保存对应 property-data；恢复封套只服务 Component/System 的最小元素状态。
这次没有修改用户 TestProject、Rendering2D 或其他创作源。

### 最终验证记录

| 验证 | 结果和日志 |
| --- | --- |
| 最终 Solution build | `/tmp/inno-recovery-closure-build-final.log`：0 warning / 0 error，退出码 0 |
| 最终完整测试集 | `/tmp/inno-recovery-closure-tests-final.log`：48 项目，800 passed / 0 failed / 0 skipped，退出码 0；基于最终二进制 no-build 复验 |
| Architecture | `/tmp/inno-recovery-architecture-final.log`：validation passed，退出码 0 |
| Reference 契约 | `/tmp/inno-recovery-contract-tests.log`：16 项通过；同样包含在最终完整测试中 |
| Scene | `/tmp/inno-recovery-scene-tests.log`：49 项通过，包含真实 Plugin remove/recover 的槽位状态校验 |
| Scene History | 最终完整测试包含新增 5 项生产链验证；此前专项 20 项通过后又加入 System 对称用例，最终均通过 |
| Support Pack | `/tmp/inno-recovery-support.log`：macOS ARM64 Release 包生成成功，退出码 0 |
| Native 文件 | miniaudio 恰好为 `native/miniaudio/macos-arm64/libminiaudio-release.dylib`，无 Debug 混入 |
| Player E2E | `/tmp/inno-recovery-player.log`：导出、初始化、运行、正常退出，整个 E2E 进程退出码 0 |
| Editor smoke | `/tmp/inno-recovery-editor.log`：临时空项目运行 30 帧，保存 editor state，完成 Dispose/BGFX shutdown，退出码 0 |

本轮临时产物根：`/tmp/inno-recovery-validation.IpmeTQ`；Support Pack 位于 `support/macos-arm64`，
Player 位于 `player/Builds/InnoPlayerE2E.app`，Editor 项目位于 `EmptyProject`。
这些测试不替代 C18 的带可视内容 Editor Play → Stop → Reload → Play 重复验收，也不代表 Windows 原生验收已完成。

中间失败没有作为基线豁免：修复了类型 Missing 时 History 不可 Undo 的生产问题；资产恢复测试同时归还原 .imeta，
避免把同路径新资产误认为原 identity；候选异常断言在独立调用帧完成后再等待 GC，避免测试自己的异常临时值保留 ALC。
Architecture 的 XML 排版检查已修正；最终无新增 known failure 或跳过。

后续优先级仍为 C07 的其余 owner 迁移，再继续 C05/C09、C06/C13/C14/C17 和 C18/C01。
本体尚未满足“全部收口后再开始玩法 Plugin”的门槛；未完成表继续保留，不批量勾选。

本次涉及的 12 个文档页面、67 个本地链接全部有效；`git diff --check` 通过。
完成提示音 `/System/Library/Sounds/Glass.aiff` 已播放，`afplay` 退出码 0。

## 夜间持续收口：发布隔离与 Audio 所有权（2026-09-08）

用户已授权在当前任务自动继续；已启用每 30 分钟的当前任务续跑（`innoengine`）。
续跑读取本报告和实施清单，继续实现范围内未关闭项；只有全部实现并验收后才暂停并给最终报告。
本节是阶段证据，不把“所有测试通过”当作 C01–C18 整体结束。

### 实际修正

1. **Shader 发布边界**：`ShaderAsset.SetDefinition` 先捕获嵌套声明、编码和依赖，再提交。
   ShaderAsset/IRModule 返回独立可编辑声明，IRPass/MaterialPassResolution/RenderMaterialPass 不暴露其内部 metadata 或 role 数组。
   既保留创作 DTO 的可编辑性，也不能通过构造输入或返回值偷改已提交的 IR/资产 bytes；引用的 canonical Asset 仍由 Identity owner 管理。
2. **Editor presentation**：删除 `EditorScenePresentationSnapshot`；`IEditorGameScenePresentation.Capture()` 直接返回
   `ContentReadScope`。Game/Scene View 原样消费统一 scope，不强持有整组 Scene；Play/Stop 后的旧 runtime identity 不会解析成 Edit 对象。
   Play lease 切换 presentation 与 RuntimeSession 退休仍是两个正确分工的阶段，没有把 Scene 销毁塞入 presentation lease。
3. **Audio 内部职责**：新 `AudioVoiceOwner` 独占调度、handle、抢占、Clip 引用和完成事件；
   `AudioContentOwner` 独占 emitter/listener 同步；`AudioDeviceCandidate` 独占未发布设备与 Mixer。
   与现有 ClipCache/MixerOwner 合作，AudioRuntime 只保留服务门面、生命周期、帧次序和设备恢复，不是拆 partial 文件。
4. **Audio Pending 退休**：Clip、Bus、Voice、Provider 未退休时保留 owner 与下层 device；
   阶段重试不重复递减 Clip 引用或提前设置 disposed。Provider 构造使用 TypeRegistry 的 `CreateExtension` 登记候选所有权，
   避免局部 catch 再发明清理协议；候选/设备超时由共享 TypeRegistry retirement barrier 保留并 Fault。
5. **完成背压与旧句柄**：事件未成功入队时保留终止中的 Voice 并在安全点重试；中立终态历史有界，
   设备替换后旧句柄仍能查询 Completed，但不能 Stop/Pause/控制新设备。

本次新增 4 项 Shader 发布负向测试、6 项 Audio 退休/背压测试，迁移既有 Editor presentation 测试。
测试不使用 IVT、反射穿透或生产测试后门。

### 本次审计中明确留下的下一批目标

- Rendering `RenderExtensionRegistry.RequestProviderGeneration/Generation`、`RenderRuntime.ActiveGeneration`、
  reload Complete/Rollback 和 `ReleaseRendering` 仍有“先清字段/标记 disposed，再 using 临时 LifetimeScope”的路径。
  必须改为持久 owner、Pending 前不 Finish、不释放 GPU 下层；RenderRequestProvider/RenderPipeline 的 Dispose hook 也需要对称重试语义。
- C07 的 Assets 全量 candidate、Graph、Settings、Plugin availability 仍未接入共同 Missing-slot Recovery 批次。
- C09 的 Rendering 资源服务 owner 尚未拆完；C05 仍需完成其余发布面深层审计。
- Audio Content collector 的全量 admission/候选隔离、Job/Render/退休队列指标与长压矩阵属于 C13/C14 后续项，
  不能用本次完成事件背压用例替代整个容量矩阵。
- C17 完整架构负向用例、C18 可视内容 Play→Stop→Reload→Play、C01 全当前 Wiki 审计仍未关闭。

本次没有触碰外部 TestProject/Rendering2D 创作源，也没有创建实际玩法 Plugin；跨平台原生验收继续排除。

### 本批次最终验收与续跑基线

| 检查 | 最终证据 |
| --- | --- |
| Solution build | `/tmp/inno-overnight-build-verified.log`：0 warning / 0 error，退出码 0 |
| 全量回归 | `/tmp/inno-overnight-tests-verified.log`：48 项目、810 passed / 0 failed / 0 skipped，退出码 0 |
| Architecture | `/tmp/inno-overnight-architecture-verified.log`：validation passed，退出码 0 |
| Release Support Pack | `/tmp/inno-overnight-support-verified.log`：macOS ARM64，退出码 0；包含唯一 miniaudio Release dylib |
| Player E2E | `/tmp/inno-overnight-player-verified.log`：最终 Support Pack 导出、实际运行和整个 E2E 进程退出码 0 |
| Editor 实机 smoke | `/tmp/inno-overnight-editor.log`：空项目 30 帧，保存 editor state，完成 Dispose 与 BGFX shutdown，退出码 0 |
| 文档/格式 | 修改页面本地链接检查通过；`git diff --check` 通过 |

产物位于 `/tmp/inno-overnight-validation.DaNrgl`，最终分发包为 `support-verified/macos-arm64`，
Player 为 `player-verified/Builds/InnoPlayerE2E.app`。完整测试已在最后源码修正后的 Debug 二进制上重新运行；
没有保留中间的测试失败或把失败用例跳过。

中间回归已纠正：设备切换要保留中立 Completed 查询而非清空终态；Candidate 清理失败必须同时阻止 Audio admission；
Play presentation 切换不等于其 RuntimeSession 已退休。公开行为按真实 owner 契约验证，没有放宽 Pending 或身份要求。

整体任务未结束，当前续跑保持 ACTIVE；最终完成提示音留到整体验收完成时。下一批从上方明确列出的
Rendering Pending 退休链与 C07 全域 Recovery 继续，不因本批 810 项全绿而停止。

## 夜间续跑：Rendering 退休链（2026-09-08）

本批继续处理上一节已确认的 Rendering Pending 缺口，没有把完整收口标准降为“当前测试全绿”。

### 实际实现与边界

- `RenderRequestProvider`、`RenderPipeline` 的 Dispose hook 在 Pending 时保持可重试；普通错误仍一次性报告。
- Provider 部分构造直接登记共享 `TypeRegistry.CreateExtension` ownership；Provider snapshot 持有长期
  `LifetimeScope`，不在 Dispose 中先清集合再临时创建 owner。
- 新内部 `RenderPipelineGeneration` 同时负责候选与活动 Pipeline/Feature；Feature 在配置前被 owner 接管，
  失败候选与旧 generation 通过共享退休屏障释放，不再使用独立 candidate/active transfer 标志。
- reload Complete/Rollback 不在 Pending 的 finally 中 Finish；当前事务、旧代或候选引用保留到实际退休。
- 新内部 `RenderRetirementQueue` 保存有序退出步骤和普通错误；Pending 时保留当前步骤，成功步骤不重复调用。
  Runtime 先退休扩展，再释放 targets/uploads/resources，最后结束设备安全帧；注入的共享 Device 始终由上层 owner 负责。
- 离屏 Target、Upload page、Resource service 的退出不再先标 disposed 或清分配表；readback cancellation
  完成前不会让等待者假结束或释放下一层资源。普通错误即使先于 Pending 出现，也保留到最终聚合。
- 帧内候选退休异常穿过 request/provider/feature/contributor 隔离 catch；退休超时不继续正常 EndFrame。
  shutdown 和帧内两个入口都验证 Fault 后拒绝 Submit、Contributor 注册和新 reload；稍后依赖空闲也不能绕过终止性 barrier。
- 初始 Contributor 列表在注册 extension owner 前验证非空项与重复实例，避免构造失败留下已注册的半初始化 runtime。

没有新增公开领域类型，没有 IVT/反射穿透/生产测试后门，没有新 Plugin、旧 API alias 或 legacy fallback。
公开行为变化为明确的 Pending 重试、终止 Fault admission 和 Contributor 参数验证，项目 Wiki 与 XML 同步说明。

### 未关闭范围与继续顺序

1. C07：Assets 全量 importer/canonical candidate、Graph、Settings、Plugin availability 的共同 Missing-slot Recovery 批次。
   目前生产 `ReferenceRecoveryTransaction` 仍主要由 SceneReloadRecovery/SceneReloadStateTransfer 消费；
   不把共享 generation gate 等同于共同 Recovery。Foundation Graph/Settings 不能反向引用 Content，需要从上层 owner 组合。
2. Rendering 正常帧内的资源替换、显式 Release、sweep 与部分 native 创建失败仍需逐项核对 ownership；
   本批的完整退出队列不能替代所有运行期 GPU 失败矩阵。C09 大型 Resource service 的职责拆分仍未完成。
3. Audio Content collector 候选隔离/容量、Job/Render/退休队列统计与长压矩阵仍属 C13/C14 后续项。
4. C05 其余 published DTO 深层不可变性，C06 真实 callback/Task 与跨域强引用 soak，C17 架构负向矩阵，
   C18 带可视内容 Editor Play→Stop→Reload→Play，C01 全量当前 Wiki 仍须继续。

跨平台原生验收保持本次排除项。真实空项目 Editor 和 Player E2E 是本批回归证据，不替代上面的可视内容验收。

### 本批最终验收与新的续跑基线

本批新增 **13 项 Rendering 回归**，覆盖 Provider 重试、Feature→Pipeline 依赖退休、失败候选 last-good、
reload Complete/Rollback、Target 普通错误跨 Pending 保留、shutdown/帧内终止 Fault、初始 Contributor
验证、Maintenance frame、Upload page 与 readback cancellation。终止 Fault 用例通过真实公开扩展/设备边界
报告 `RetirementTimeoutException`，验证传播和不可恢复性；实际计时 deadline 仍由已有 Core 测试覆盖，
不将注入失败冒充真实 GPU 挂起或 callback 长压验收。

| 检查 | 最终证据 |
| --- | --- |
| Solution build | `/tmp/inno-render-closure-build-verified.log`：0 warning / 0 error，退出码 0 |
| 全量测试 | `/tmp/inno-render-closure-tests-verified.log`：48 项目、823 passed / 0 failed / 0 skipped，退出码 0 |
| Rendering Runtime | 上述全量结果内 41 项通过，包含原有真实 collectible Plugin 移除/恢复回收测试 |
| Architecture | `/tmp/inno-render-closure-architecture-verified.log`：validation passed，退出码 0 |
| macOS Editor | `/tmp/inno-render-closure-editor.log`：隔离空项目 30 帧、editor state 保存、Dispose 与 BGFX shutdown，退出码 0 |
| Release Support Pack | `/tmp/inno-render-closure-support.log`：macOS ARM64 产物生成，退出码 0；仅有正确的 miniaudio Release dylib |
| 实际 Player E2E | `/tmp/inno-render-closure-player.log`：使用本批 Release Pack 导出、实际运行、BGFX shutdown 和整体进程退出码 0 |
| 文档与格式 | 修改页面的本地链接与 `git diff --check` 通过 |

最终发布产物位于 `/tmp/inno-render-validation.QvwS2r/support/macos-arm64`，
Player 为 `/tmp/inno-render-validation.QvwS2r/player/Builds/InnoPlayerE2E.app`。
本批没有操作外部 TestProject 或 Rendering2D 创作目录，没有新增已知测试失败或跳过用例。

当前任务仍未整体完成，自动续跑保持启用，最终完成提示音仍留到整体验收时。
下批优先继续 C07 共同 Recovery 和上面已定位的运行期资源所有权缺口；不得把本节的 823 项通过当作全部关闭。

## 夜间续跑：Asset Source Recovery 与 canonical 退休（2026-09-08）

本批沿 C07 进入实际 Source Mount 生产路径，同时补齐检查中发现的 Asset 退休所有权问题。
这不是 Assets 全域候选事务已完成的声明。

### 实际实现

- `AssetLoader` 的 Source 候选保持 detached：预加载对象只有 persistent ID，没有 runtime ID。
  激活原子切换 canonical loader 与文件索引注册；注册冲突恢复旧 domain，不让候选抢占活动对象。
- `AssetSourceMountRecovery` 真实参与 `ReferenceRecoveryTransaction`：捕获 loaded canonical 与
  ID tombstone 的中立槽位，发布后解析并验证；Imported 记录无法物化则拒绝，真正缺失则保留 Missing。
  source 与原 `.imeta` 返回后恢复同一槽；同路径新 identity 不替代旧引用。
- 唯一新增的公开读入口为 `AssetSourceMountTransaction.recoveryChanges`，提供无 live object 的只读
  解析结果，供 Host 验证与诊断；显式不进入游戏脚本 API。没有新增中央类型表或平行引用协议。
- Source Complete/Rollback、失败准备与 Pipeline shutdown 复用 Core `LifetimeScope`/`RetirementBarrier`。
  只有退休终止后才清掉 owner。超时保留两代资源、Fault 共享 gate，并封锁后续访问与依赖销毁。
- `AssetObject.OnUnloading` Pending 不再提前标记 released 或清空 payload；终止失败仍明确向上报告。
  `AssetLoader` 的退休包含 ID-only tombstone，并保留普通失败跨 Pending 的诊断。
  Loader 关闭准入后等待已执行/排队的操作退出，不阻塞在同步 Dispose 中，也不提前销毁操作 gate。
- Player `AssetDatabase` 同步使用持久 lifetime 退休 canonical object；Pending 时保留 payload、Identity、
  dependency retention 和下层读取 gate。预算 eviction 不再提前丢掉 dependency retention；退休中的
  loaded record 不会继续作为可使用实例交付。

### 本批回归与中间失败

新增 14 项 Asset Pipeline/Runtime 公开边界回归：Source 激活/回滚、未发布 Identity、注册冲突补偿、
原 metadata 移除/恢复、短暂 Pending、终止 timeout、失败聚合、同步/异步退出、ID tombstone 与
从真实部署 Artifact 加载的 Runtime Database 退出。Asset Pipeline 测试数从 56 增为 70。
终止 timeout 通过公开 unload hook 注入，真实计时 deadline 继续由已有 Core 测试覆盖。

首轮全量测试发现 7 个 Build 与 1 个 Audio scripting 用例失败，都是本批引入的退出线程限制，
不是基线失败：shutdown barrier 错绑到构造线程，异步测试在后续线程退出时触发 Fault。
已改为在首次退出时建立退休线程所有权，保持原本 thread-neutral 的最终 Dispose 契约；
Source Mount 激活仍严格由构造线程驱动。没有删除/跳过测试或放宽断言。
本节的最终通过状态以下方复验结果为准，不能使用中间运行的部分 passed 数量。

### 后续必须继续的已定位缺口

1. **C07**：`AssetCatalogTransaction` 更换 Importer/type 时仍直接重扫 live loader，失败仅延迟恢复；
   必须实现真正隔离候选与共享 rollback，不与已修正的 Source Mount 事务混为一谈。
   Settings 的 `m_effective` 仍强持有 `ISerializable` 扩展实例，且 Compose/Clone 仍有缺少 owner context
   的序列化入口。Settings/Graph/Plugin availability 仍需共同生产 Recovery，Foundation 不反向引用 References。
2. **Assets 资源矩阵**：完整 import/load 失败回滚、运行期替换与异步取消的组合场景继续审计；
   本批 canonical 最终退出不能替代全部失败路径。特别核对 aggregate 内嵌 Pending 不得被当作普通失败。
3. **C09/C13/C14**：Rendering 常规帧资源 replacement/sweep/native allocation 失败与 Resource service
   的真实 owner 拆分；Audio Content Provider 部分贡献隔离/容量，以及 Job/Render/退休队列的统计和长压。
4. **C05/C06/C17/C18/C01**：其余深层不可变 DTO、真实 callback/Task 跨域 ALC soak、完整架构负向矩阵、
   有可视内容的 Editor Play→Stop→Reload→Play、全量当前 Wiki 签名与示例核验。

自动续跑继续启用。跨平台原生验收、实际玩法 Plugin 与 Physics 仍不进入本次范围。

### 本批最终复验与新基线

| 检查 | 最终证据 |
| --- | --- |
| Solution Debug build | `/tmp/inno-source-closure-build-reverified.log`：0 warning / 0 error，退出码 0 |
| 全量测试 | `/tmp/inno-source-closure-tests-reverified.log`：48 项目、837 passed / 0 failed / 0 skipped，退出码 0 |
| 定向关键项目 | 上述全量结果中 Asset Pipeline 70、Build 23、Audio 6、Rendering Runtime 41 项全部通过 |
| Architecture | `/tmp/inno-source-closure-architecture-reverified.log`：validation passed，退出码 0 |
| 真实 macOS Editor | `/tmp/inno-source-closure-editor-verified.log`：隔离项目 30 帧、状态恢复/保存、Dispose 与 BGFX shutdown，退出码 0 |
| Release Support Pack | `/tmp/inno-source-closure-support.log`：macOS ARM64 生成成功，退出码 0；无 Authoring Pipeline DLL，只有正确的 miniaudio Release dylib |
| 真实 Player E2E | `/tmp/inno-source-closure-player-verified.log`：最终 Debug authoring 工具导出并实际运行 Release Player，BGFX shutdown 与整体进程退出码 0 |
| Wiki/格式 | 本批修改页面相对链接检查与 `git diff --check` 通过 |

Support Pack 位于 `/tmp/inno-source-validation.oJsz0h/support/macos-arm64`，
最终 Player 位于 `/tmp/inno-source-validation.oJsz0h/player-verified/Builds/InnoPlayerE2E.app`。
中间的旧 DLL E2E 运行同样暴露了上述退出线程问题；它不算通过证据，最终以 `player-verified` 为准。

本批没有留下新增已知测试失败，没有操作外部 TestProject/Rendering2D 项目，没有新增 legacy 入口。
837 项通过是接下来续跑的基线，不是全项目已经收口的声明。剩余整项见上一节，最终完成提示音仍保留到整体验收。

## 夜间续跑：Settings 中立缓存与完整 owner context（2026-09-08）

本批关闭上一节明确记录的 Settings 实例缓存与缺失 resolver 问题，不将其等同于 C07 全域 Recovery 已完成。

### 实际实现

- `ProjectSettings` 的 effective cache 改为 Stable Type ID + property bytes，不持有扩展 `ISerializable` 实例。
  每次查询通过当前 definition 构造快照；类型暂缺或不匹配时返回不可用，原 bytes 不丢失。
- `ProjectSettings` / `ProjectSettingsStore` 构造明确要求 owner `SerializationContext`；Compose、Clone、
  replacement、contribution capture/restore 全部使用同一 context，不添加旧构造入口或临时 resolver fallback。
- Editor / Build 先建立 AssetPipeline，再创建 Settings；Player 在 RuntimeSession 建立 AssetDatabase 后、
  创建 subsystem 前创建 Settings。Player 不再在 Audio settings 不可用时静默构造默认配置。
- `ProjectSettingsContributor` 深复制入参和读出口中的 record/property bytes，dependency/override 列表冻结。
  候选组合失败保留 last-good effective snapshot 与 revision；返回的普通属性快照彼此独立。
- Runtime AssetDatabase 的外部 reference resolver 可以物化 catalog 内尚未加载的资产，并经过正常 residency
  准入与 session pin。内部资产 hydration 仍使用 prepared-only resolver，不能靠此变更绕过部署依赖闭包。
- Asset reference 通用协议不再把 `RetirementPendingException` 当成 Missing；所有权尚未退休是 barrier，
  不是内容暂缺。嵌套 Aggregate 及其余领域退休矩阵仍需继续审计。

新增 9 项测试：7 项 Settings/Asset 场景（含 replacement 与 composer 两条路径的 missing/restore、
真实 Runtime artifact 冷引用、深不可变与失败 last-good），1 项真实脚本 ALC 退出，1 项 Pending 协议传播。
真实 ALC 用例让一个未被 ScriptReloadHost 显式 Rebuild 的第二 Settings store 跨代存活，验证旧脚本类型仍可回收。
中间一次失败发生在新 generation 的 fixture teardown：测试末尾 `out _` 临时值延长了新实例生命期；
改为已有 no-inline 弱引用观察边界后通过，未放宽 GC deadline、删除断言或跳过测试。

### 本批验收

| 检查 | 证据 |
| --- | --- |
| Solution Debug build | `/tmp/inno-settings-owner-build-verified.log`：0 warning / 0 error，退出码 0 |
| 全量测试 | `/tmp/inno-settings-owner-tests-verified.log`：48 项目、846 passed / 0 failed / 0 skipped，退出码 0 |
| Architecture | `/tmp/inno-settings-owner-architecture-verified.log`：validation passed，退出码 0 |
| 真实 Editor | `/tmp/inno-settings-owner-editor.log`：隔离空项目 30 帧与正常退出，退出码 0 |
| Release Support Pack | `/tmp/inno-settings-owner-support.log`：本批 Player 初始化顺序生成 macOS ARM64 Release 产物，退出码 0 |
| 真实 Player E2E | `/tmp/inno-settings-owner-player.log`：导出并运行本批 Release Player，退出码 0 |

产物位于 `/tmp/inno-settings-validation.34dop1`。项目 Wiki 同步说明所需 owner context、冷引用与调用者的
generation-local 快照责任。Foundation Settings 没有反向引用 Assets/References。

**未关闭**：Settings/Graph/Plugin availability 的共同 Recovery participant、Assembly Catalog/Importer 隔离候选、
运行期 GPU 资源 owner/失败矩阵、Audio collector 与其余队列预算、真实跨域泄漏长压、完整架构负向矩阵、
可视内容 Play/Stop/Reload/Play 以及全量 Wiki 核验。846 项通过不是整体验收；自动续跑仍启用。

## 夜间续跑：Audio 内容整批接纳与编译符号负向矩阵（2026-09-08）

### 实际实现

Audio Provider 原先共享同一个无界 collector，失败前已提交的数据仍会被同步。本批改为每个 Provider
独立暂存，成功返回后统一验证，再整批接纳到本帧；跨 Provider 重复会拒绝后者全部贡献，不抢占较早成功的
identity，也不占用后续 Provider 的预算。失败 Provider 本帧缺席的旧 emitter 按常规 absent-content 规则停止。
这里的原子性是 **内容贡献接纳**，不是将整帧 native Voice/Listener 创建包装成硬件事务。

- `AudioRuntimeOptions.maxContentSnapshots`：默认 1024，控制 emitter + listener 合计的帧级容量；构造时复制。
- `AudioContentProviderContext`：限定 owner thread；`capacity` 为本次剩余预算；任意 default/duplicate/overflow
  提交使整批失效，Provider 内部吞异常不能绕过。返回后 Dispose 撤销读写并清掉 Clip 引用，不拥有借用的 host scope。
- `AudioContentStatistics` / `AudioRuntime.contentStatistics`：提供最近 collection 的容量、接纳 snapshot 数和
  拒绝 Provider 数。必要性是让 Host 能观察此独立预算；不增加设备 ABI、游戏 façade 或中央脚本名单。
- `AudioRuntime.Update`：在执行 Provider 前拒绝负数、NaN 和无限 deltaTime。
- direct `RetirementPendingException` 继续穿过 Provider 隔离边界，不被记成普通失败或忽略。

新增 17 项 Audio 测试，包括 default/duplicate/capacity、冻结快照、跨线程拒绝、撤销后的 GC 引用释放、
Provider 部分失败、后续 identity/预算重用、256 帧连续超限、错误时间和 Pending 传播。
Audio Core 项目共 15 项（含原有 scripting 编译），Audio Runtime 项目共 29 项通过。

Architecture 增加 39 项 **真实编译 DLL → 正式 CLI** 的正反例，不调用工具 internal API：
成员/继承/接口/事件/delegate、generic constraints、数组/tuple、nested generic container、unsafe/function pointer、
有效可见性、assembly identity、Host adapter 与 Editor public reference、SDL3 consumer。
测试驱动修复 `PublicApiBoundaryValidator` 未追踪 nested type 的封闭泛型外层，以及 SDL3 ProjectReference
规则误用 `SDL3` 而非当前 `Sdl3` 名称的漏检。SDL3 Platform 与相应 ImGui presentation、toolchain 是允许的实现 owner。
原有 5 项源码 AST fixture 保留，Architecture.Tests 共 44 项通过。

同时修正 `IAssemblyCatalogTransaction.Complete` XML 中“忽略清理错误”的过时描述：当前行为必须传播并 Fault，
Pending 必须保留所有权。没有添加兼容入口、放宽清理规则、缩短 GC 验证或隐藏失败用例。

### 本批最终验收

| 检查 | 最终证据 |
| --- | --- |
| Solution Debug build | `/tmp/inno-content-symbol-build-verified.log`：0 warning / 0 error，退出码 0 |
| 全量测试 | `/tmp/inno-content-symbol-tests-verified.log`：48 项目、902 passed / 0 failed / 0 skipped，退出码 0 |
| Architecture | `/tmp/inno-content-symbol-architecture-verified.log`：validation passed，退出码 0 |
| 真实 macOS Editor | `/tmp/inno-audio-content-editor.log`：隔离空项目 30 帧、状态保存与正常 BGFX shutdown，退出码 0 |
| Release Support Pack | `/tmp/inno-audio-content-support.log`：macOS ARM64 产物生成，退出码 0；只有 miniaudio Release dylib，无 Authoring AssetPipeline DLL |
| 真实 Player E2E | `/tmp/inno-audio-content-player.log`：本批 Release Pack 导出、实际运行、正常 shutdown，退出码 0 |
| Wiki/格式 | 检查涉及的 47 页、212 个本地链接，未发现断链；`git diff --check` 通过 |

Support Pack 与 Player 位于 `/tmp/inno-audio-content-validation.E8G9Od`。Settings 的 846、Audio 的 863
是中间成功基线，本批最终继续基线是 **902**；不能将中间日志或旧 DLL 当作最终综合验证。

### 必须继续的剩余工作与本次定位

1. **C07**：Assembly Catalog/Importer 仍重扫 live loader 并延迟 rollback 恢复；需要与既有 Source Mount
   隔离候选合并所有权，处理 Plugin 已有 pending source candidate，不能重复创建第二个候选或重扫旧代。
   Settings/Graph/Plugin availability 仍需共同 Recovery participant。此次 Settings 中立缓存与 Audio collector
   并未替代这个生产协议。
2. **C09/C13/C14**：Rendering 常规 replacement/Release/sweep/部分 native 创建失败与大型 Resource service
   真实 owner 拆分；Job/Render/retirement 队列预算与长压。Audio collector 子项已关，其余 Audio 失败矩阵仍需继续。
   新定位：`AudioVoiceOwner.Play` 在验证/取得新 Clip request 前会先 stealing；无效请求可能影响旧 Voice。
   `AudioPlayOptions`/`AudioVoiceParameters`/spatial 值仍有只比较范围、未拒绝 NaN/Infinity 的入口，需要统一边界校验。
3. **C17**：本批静态符号矩阵已补齐上述明确形状；**ImGui 仍有真实 native public surface**：
   Editor ImGui façade 直接使用 native flags，`Properties/ScriptingApi.cs` 映射/导出 native enum，
   `ImGuiWidget.GetGlyphVisualBounds` 接收 `ImFontPtr`。需要中立 UI 契约与调用方整体迁移，不能将 ImGui 排除规则说成已经封装。
4. **C05/C06/C18/C01**：全域深不可变 DTO、nested Aggregate Pending、失败启动 ownership、真实 callback/Task
   跨域 ALC 长压、有可视内容的 Editor Play→Stop→Reload→Play、全量当前 Wiki 签名与示例核验。

以上是未完成工作，不是接受的豁免。跨平台原生验收、实际玩法 Plugin 和 Physics 才是用户指定的范围外项。
夜间续跑保持启用，最终提示音仍留到完整要求真正完成时。

## 夜间续跑：Audio 接纳与共享 Residency 退休（2026-09-08）

### 实际修正

1. **参数统一验证**：播放 volume/pitch/pan、全部 spatial 标量与向量、listener 各向量均拒绝非有限值；
   load mode/distance model 拒绝未定义值。现有范围与有限零向量语义保留。默认 struct 绕过构造函数，因此
   Runtime 在 Play 接纳前显式拒绝无 Bus 的 options，Voice 参数更新拒绝默认零 pitch。
   MiniAudio/Muted 对直接设备 Play、schedule、Bus volume 使用相同规则，非法请求不分配 native Voice。
2. **Voice 接纳与背压**：先构建 Artifact request 再抢占。无效 options 与直接 acquisition Pending 不影响旧 Voice；
   被背压拒绝的新请求经 TypeRegistry/Core retirement barrier 清理，不遗失其 Artifact。终止中的被抢占 Voice
   重试完成阶段，不重复 Stop、Clip 引用计数或 stolen 统计。
   普通 Missing/metadata/解码失败仍采用 Preparing → DecodeFailed 事件协议；不承诺异步解码失败不消耗预算。
3. **共享租约所有权**：`AssetLease<T>`/`ArtifactLease` 统一委托内部 `ResidencyLease<T>`。
   直接 Pending 保留 value/callback；成功或普通终止失败才撤销。并发/重入释放返回 Pending，不提前冒充完成；
   callback 在内部锁外执行。`RetentionScope` 删除独立清理算法，直接复用 Core `LifetimeScope`。
4. **真实 Runtime Database eviction**：每个 lease 单独记录计数是否已释放；预算回收 Pending 时可以重试 Trim，
   不重复递减计数、不释放其他租约，不丢 canonical runtime Identity 与 payload。
5. **Audio Cache 分阶段退休**：每个条目用 Core lifetime 先退休 native Clip，再释放 Artifact。
   Artifact Pending 重试不会再次 DestroyClip；退出中的缓存条目不再复用。ReleasePreload 重试先完成此前退休，
   不误消费另一 load mode 的 reservation；Update 安全点继续排空待退出条目。
   Update 中取消/失败的 preload 保留 waiter 到清理完成，不把 Pending 记成解码失败。
   缓存命中的临时 request 额外租约也进入共享 retirement barrier。

没有新增 public API、Native ABI、中央扩展类型表、测试后门或兼容 overload。
公开行为的变化是严格参数拒绝和明确的 Pending 所有权，项目 XML/Wiki 与专项标准已同步。

### 测试与排错

新增 **32 项**：Audio Core 11、Audio Runtime 12、MiniAudio 1、Assets 租约 7、真实 Runtime Database 1。
Audio Runtime 共 41 项、Core 共 26 项、MiniAudio adapter 共 4 项，Assets/Pipeline 分别 27/78 项通过。
覆盖内容包括全量非法数值入口、无效 options、真实 native no-device 拒绝后继续播放、抢占/事件背压重试、
Artifact acquisition/释放 Pending、preload 取消、部分退休缓存拒绝复用、多 load mode reservation、
并发释放、普通失败、LIFO 与 GC 弱引用验证。

首轮新背压用例使用了无效默认 Voice handle 来填满 EventDispatcher，被现有事件构造参数校验提前拒绝。
已改成由公开 `AudioVoiceAllocator` 分配有效 handle，全部背压断言保留；之后重跑定向与整个 solution。
这不是忽略的基线失败，也没有削弱事件契约。早期测试阻塞 await 的 analyzer warning 已改为 await 已完成任务，
最终构建 0 warning；真实数据库仍由原 owner thread Pump 完成发布。

### 本批最终验收

| 检查 | 最终证据 |
| --- | --- |
| Solution Debug build | `/tmp/inno-audio-residency-build-verified.log`：0 warning / 0 error，退出码 0 |
| Audio Runtime 定向 | `/tmp/inno-audio-residency-runtime-verified.log`：41 passed，退出码 0 |
| 全量测试 | `/tmp/inno-audio-residency-tests-final.log`：48 项目、934 passed / 0 failed / 0 skipped，退出码 0 |
| Architecture | `/tmp/inno-audio-residency-architecture-final.log`：validation passed，退出码 0 |
| 真实 macOS Editor | `/tmp/inno-audio-residency-editor.log`：隔离空项目 30 帧、状态保存、正常 shutdown，退出码 0 |
| Release Support Pack | `/tmp/inno-audio-residency-support.log`：macOS ARM64 本批 Release 产物生成，退出码 0 |
| 真实 Player E2E | `/tmp/inno-audio-residency-player.log`：导出并实际运行本批 Pack，退出码 0 |
| 文档与格式 | 涉及的 9 页/45 个本地链接无缺失；`git diff --check` 通过 |

原生验收产物在 `/tmp/inno-audio-residency-validation.nKsWnh`，只有 miniaudio Release dylib，
没有 Authoring `Inno.Assets.Pipeline.dll`。首次全量运行的失败日志保留于
`/tmp/inno-audio-residency-tests-verified.log`，**最终证据必须使用 `tests-final.log`，不是这个初次日志**。

### 下一批仍必须继续

- **C07**：Assembly Catalog/Importer 的 live Rescan/延迟 rollback 尚未替换成隔离候选；须与现有 Plugin Source
  Mount candidate 合并所有权。Settings、Graph、Plugin availability 的共同 Recovery participant 也未完成。
- **C09**：Rendering 的常规 replacement、Release、sweep、部分 native 创建失败与 Resource service owner 拆分。
- **C05/C06**：全域不可变 DTO、嵌套 Aggregate 中 Pending 的分类/传播、失败启动所有权、实际 callback/Task 跨 ALC 长压。
  本批共享 lease 只补齐直接 Pending；不能因此宣称嵌套异常或所有 generation 保留问题都已关闭。
- **C13/C14**：Job/Render/retirement 队列与压力矩阵。Audio 的接纳/缓存已补上述子项，仍需继续审计 backend 查询
  在 preload 引用计数增加后异常的失败回滚等其余部分启动路径，不能把一个正常后端的回归当成所有可替换 backend 的证明。
- **C17**：ImGui flags、ScriptingApi 中 native enum 和 `ImFontPtr` 公开入口仍须中立化并迁移所有调用方。
- **C18/C01**：带可视内容的 Editor Play→Stop→Reload→Play 连续验收，全量当前 Wiki 签名与示例核验。

这些不是豁免项；自动续跑保持启用。跨平台原生验收、实际玩法 Plugin 和 Physics 仍在本任务范围外。
整体完成前不宣布最终成功，也不提前播放最终完成提示音。

### 同次继续：Preload 准备失败回滚

没有将刚发现的 `GetClipState` 失败分支留给用户。本次接着完成：先成功查询准备状态，再增加 preload 引用；
查询异常/Failed 只退休本次新建且无人引用的 cache entry，命中已有条目时不改变它原来的 Voice/preload 引用。
缓存清理与临时 request 清理分别保留原准备异常，普通回滚失败与原错误一起报告并 Fault，不互相覆盖。
Pending 仍经共享 barrier 处理，没有另建清理队列。

再增加 5 项测试：新 Clip/已有 Clip × 查询异常/Failed 的四格矩阵，外加查询失败与 native 回滚失败同时出现时
保留双诊断并封锁 generation。每个成功回滚用例都继续合法 preload/release，验证缓存没有留下隐藏 reservation。
**Audio Runtime 共 46 项，全套从 934 增至 939 项**；本次总新增 37 项。上一节“backend 查询后残留引用”的具体
未关闭点已在此关闭，C13/C14 的其余队列、峰值与领域组合矩阵仍未关闭。

| 最新检查 | 证据 |
| --- | --- |
| Debug Solution build | `/tmp/inno-audio-query-build-final.log`：0 warning / 0 error |
| Audio Runtime | `/tmp/inno-audio-query-runtime-final.log`：46 passed，退出码 0 |
| 全量测试 | `/tmp/inno-audio-query-tests-final.log`：48 项目、939 passed / 0 failed / 0 skipped，退出码 0 |
| Architecture | `/tmp/inno-audio-query-architecture-final.log`：validation passed，退出码 0 |
| 真实 Editor | `/tmp/inno-audio-query-editor.log`：新空项目 30 帧、保存、正常 shutdown，退出码 0 |
| 新 Release Pack | `/tmp/inno-audio-query-support.log`：本次修复重新生成，退出码 0 |
| 真实 Player E2E | `/tmp/inno-audio-query-player.log`：用本次 Pack 导出并实际运行，退出码 0 |

最终原生产物位于 `/tmp/inno-audio-query-validation.XhuYZF`，只有 miniaudio Release dylib，
未携带 Authoring AssetPipeline。最后仅将 private PreparePreload 方法移到文件私有方法组，行为未变并重新构建。
939 是后续起点；C07、C09、C05/C06、其余 C13/C14、C17、C18/C01 仍按上一节保留，自动续跑不停止。

## Assembly Catalog / Importer 候选隔离收口（2026-09-08 继续）

本次把 Assembly Catalog participant 从“在 live loader 上 Rescan，失败后下次访问再修复”改为真实的隔离
Asset Source candidate。Prepare 保留旧 canonical 对象、Identity、payload 和 Catalog；Activate 使用共享
ReferenceRecoveryTransaction 发布候选；Complete 提升文件并退休旧 owner；Rollback 还原原 owner，
不在旧对象上执行二次导入来模拟回滚。已有 Plugin source candidate 时只借用其验证，发布和退休仍由原 owner 负责。

| 子项 | 实际实现 |
| --- | --- |
| 导入健康校验 | 候选新增或改变的可写源 import failure 拒绝 publication；last-good 及 sidecar 不变，修正源后显式重建可恢复 |
| Source metadata | `.imeta` 只写入候选中立 stage；提交先验证外部修改，再写入。后续 sidecar 或 Catalog 提升失败逆序补偿，不覆盖外部修改 |
| Catalog 提升 | 旧 journal 删除失败时恢复旧 snapshot，然后外层恢复 metadata；补偿失败保留双错误并由 publication owner Fault |
| 诊断隔离 | 候选只暂存中立 Diagnostic；激活时使用 Core DiagnosticReporter，回滚恢复旧报告；旧 owner 退出不能清除新 owner 的报告 |
| 同步访问 | `GenerationCoordinator.AcquireOperation` 与 TypeCatalog 同名转交入口保护完整同步操作；自动刷新延后，发布仅允许自己的控制线程借用 |
| last-good 可查询 | 候选失败本身不重新置 dirty；非动态、参与 Catalog 的 Host/InnoInternal Assembly load 才触发自动刷新。BCL/测试工具加载不重复触发被拒绝的候选 |

文件提升是 owner-safe-point 下带补偿的多文件提交，不声称具有文件系统跨文件 crash-atomic 保证。
新增公开入口仅上述两个基础设施 scope：正常数据操作不等于 Play/Build/Export admission；AwaitingCollection
仍禁止新代际与这些产品操作，Faulted 仍必须重启，不延长 GC 或 resource retirement timeout。

新增 16 项公开边界测试：Asset Pipeline 11 项、GenerationCoordinator 4 项、Modules 1 项。
包括后续 participant 拒绝、Plugin 候选所有权、坏导入、外部 metadata 冲突、多文件补偿、诊断提交/恢复、
owner-thread publication borrowing、GC 等待、last-good 查询及显式恢复。既有 History 5 项未弱化断言。

中途失败均作为本次回归处理，没有标成基线豁免：

- `/tmp/inno-catalog-recovery-tests-full.log`：两个 History 退休超时；自动刷新重入正在操作的旧 loader。
- `/tmp/inno-catalog-operation-tests-full.log`：History 修复后，两项 last-good 查询触发候选重试。
- `/tmp/inno-catalog-final-tests.log`：剩余一项由错误格式化加载无关 BCL 引起；AssemblyLoad 分类过滤后，Asset Pipeline 89 项通过。

最终验收结果如下。C07 整项仍不关闭：Settings、Graph、Plugin availability
的统一 Missing-slot recovery 尚未全域接通。其余 C05/C06/C09/C13/C14/C17/C18/C01 仍按前述范围继续，
不开发实际玩法 Plugin、不修改外部创作项目、不以此次局部全绿代替整机收口。

| 最新检查 | 证据 |
| --- | --- |
| Debug Solution build | `/tmp/inno-catalog-verified-build.log`：0 warning / 0 error，退出码 0 |
| 全量测试 | `/tmp/inno-catalog-verified-tests.log`：48 项目、955 passed / 0 failed / 0 skipped，退出码 0 |
| Asset Pipeline / shared gate | 全量内分别 89 / 16 passed；History 原 5 项均通过，未弱化现有断言 |
| Architecture | `/tmp/inno-catalog-verified-architecture.log`：validation passed，退出码 0 |
| 真实 Editor | `/tmp/inno-catalog-verified-editor.log`：新空项目 30 帧、保存与正常退出 |
| 新 Release Pack | `/tmp/inno-catalog-verified-support.log`：从最终源码重新生成 macOS ARM64 Pack |
| 真实 Player E2E | `/tmp/inno-catalog-verified-player.log`：最终 Pack 导出、实际 native 启动和闭包检查通过，退出码 0 |
| 文档与补丁 | 本次 9 个 Wiki/标准/报告页面、39 个本地链接无缺失；`git diff --check` 通过 |

最终原生产物位于 `/tmp/inno-catalog-verified-validation.223kuA`。新增 native 后端或 Windows 实机验收不在此批范围。
955 是后续基线，**不是停止条件**。下一批优先解决 C06 的 nested Aggregate Pending 所有权传播与失败准备，
随后继续 Settings/Graph/Plugin availability recovery、Rendering 资源 owner、其他队列预算和真实可视 Play/Reload。
自动续跑保持启用；整项尚未完成，因此不播放完成提示音。

## 完整异常树 / 取消 / 候选退休收口（2026-09-08 继续）

本次从 955 基线继续检查 C06，实际修复“直接 Pending 能阻止释放，但包装在 Aggregate/InnerException 中就被
当作普通失败”的所有权缺口，同时追到了取消、任务同步前缀、并发 Dispose 和 Plugin 回滚路径。
没有新增领域专属 Lifetime、DiagnosticSink、兼容入口或测试穿透。

| 子项 | 已实现行为 |
| --- | --- |
| 统一异常分类 | Core `RetirementPendingException.Find(Exception)` 检查完整异常树，优先识别嵌套 timeout；分类不替换原始外层异常 |
| 普通错误保留 | `CollectCompletedFailures` 按异常实例收集普通分支，Core Lifetime/Barrier、Asset residency 共用；重试成功后仍汇总之前已发生的错误 |
| 全链传播 | Layer/Stack、Runtime Subsystem/Pipeline/Session/Host、Shell、Editor 生命周期/Play、Audio/Rendering、Assets/Scene 与 Registry/Catalog/Module/Reference recovery 使用同一分类边界 |
| 多 participant 失败 | 先发生的普通 cleanup、初始 activation/prepare 错误与后续 Pending 一起保留；停止后续依赖清理，不以最后一个异常覆盖早先原因 |
| 取消回调 | Cancel 本身保存 callback 故障；callback 仍执行时不释放依赖。直接或异步 Dispose 都不能丢失之前单独 Cancel 的 Pending/普通错误 |
| Task 完成与退休不同 | Task 已完成但仍报告 Pending 时保持故障和依赖；包装 Pending 的 OperationCanceledException 发布为 faulted Task，不能通过 canceled 状态消除所有权信号 |
| 工作入口 | RunAsync 在调用 operation 同步前缀之前登记任务，前缀重入 Cancel/Dispose 不能让任务逃离 lifetime；释放链接取消注册后才发布完成 |
| 并发/重入释放 | 同一 Lifetime 只允许一个资源释放尝试；另一线程或重入调用返回 Pending，不重复调用 Dispose，不持内部锁执行资源 callback |
| Plugin 候选 | Prepare/initial activation/observer/Activate 不将包装 Pending 隔离为 unavailable；观察者报告 Pending 后停止后续通知，保留候选。Rollback 全部阶段成功后才清空候选字段 |
| 构造失败订阅 | PluginEnvironment 初始激活成功后才创建 watcher，避免初始候选失败时遗留不可达文件系统订阅 |
| Architecture | AST 拒绝直接 catch Pending/Timeout（含全限定名），要求统一 Exception filter；两项反例和合法 filter 正例均已执行 |

新增公开入口只有上述两个 Core 分类/汇总方法；Pending/Timeout 构造器增加可选 innerException，保留原因链。
它们属于基础设施错误协议，不是 Scripting API 或第二套 generation gate。Runtime 与 Editor 的公开易用入口没有扩张。
Play Mode 对嵌套 timeout 同样进入终止故障，不能当成短暂 Pending 下一帧继续。GC unload 的完整成功条件没有降低。

新增 **41 项公开边界回归**：Core Execution 15、GenerationCoordinator 6、真实 Modules ALC 6、
References 6、Assets residency 2、Plugins 3、Architecture 3。既有断言没有删除、跳过或将失败声明成基线豁免。
涉及 rollback 异常类型的既有测试改为同时验证原始 activation 错误和 Pending，不再要求丢失前者后只抛最后一个异常。

真实 ALC 测试分别覆盖包装 Activate/Complete/Rollback、多个 participant 的普通失败后 Pending、以及 activation
失败后 rollback Pending；观察旧/候选 context 的 Unloading 均未提前发生，重复 Dispose 仍被同一未退休 owner 阻止。
Plugin 回调测试通过真实 public events 和 Asset source candidate 断言候选保留；模拟回调静止后才显式回滚，
不以此声称 Faulted Host 可以重启 generation，也未增加恢复 Fault 的测试专用后门。

### 本批最终验收

| 检查 | 最终源码证据 |
| --- | --- |
| Solution build | `/tmp/inno-retirement-complete-build.log`：0 warning / 0 error，退出码 0 |
| 全量测试 | `/tmp/inno-retirement-complete-tests.log`：48 项目、996 passed / 0 failed / 0 skipped，退出码 0 |
| Core / Modules / shared gate | 同一全量结果内分别 26 / 30 / 22 passed |
| References / Plugins / Architecture tests | 同一全量结果内分别 22 / 31 / 47 passed |
| Architecture 实际仓库 | `/tmp/inno-retirement-complete-architecture.log`：validation passed，退出码 0 |
| 新 Release Pack | `/tmp/inno-retirement-complete-support.log`：macOS ARM64 Pack 重建成功，退出码 0；miniaudio 仅正确的 Release dylib |
| 真实 Player E2E | `/tmp/inno-retirement-complete-player.log`：最终 Pack 导出、Metal/BGFX 实际初始化与退出、closure 检查通过，退出码 0 |
| 真实 Editor | `/tmp/inno-retirement-complete-editor.log`：新空项目 30 帧、保存 editor state、正常 Dispose/native shutdown，退出码 0 |
| 文档和补丁 | 12 个本轮项目页/标准/报告页面的 47 个本地链接无缺失，`git diff --check` 通过 |

最终原生产物位于 `/tmp/inno-retirement-final-validation.NXJJbi`。过程中 975 项快照及后续编译日志只作为中间证据；
本节 996 项和上述 complete 日志才是本批最终基线。

### 后续仍须执行，不是整项完成声明

- C06 的嵌套 Pending 子项已补，但失败 startup 的剩余组合、真实设备 callback/Task 的强引用长时 soak 尚未全覆盖。
- C07 Settings / Graph / Plugin availability 仍须组合到共同 Missing-slot Recovery；当前 Plugin 变更只修退休/回滚所有权。
- C09 Rendering 大型资源 owner 分解与替换/释放/失败矩阵；C05 其他 immutable 发布面的全仓审计。
- C13/C14 其他队列、Jobs、长时压力；Plugin 后台 reconciliation Task 的完整退出 ownership 仍需后续统一处理。
- C17 ImGui native public surface；C18 有可视内容的 Play → Stop → Reload → Play 重复验收；C01 其余历史 Wiki 签名/示例审计。

跨平台原生验收继续按用户范围排除，不开发玩法 Plugin/Physics，不改外部创作项目。自动续跑保持启用；
996 不是停止条件，未播放整项完成提示音。
