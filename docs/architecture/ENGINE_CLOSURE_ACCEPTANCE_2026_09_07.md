# 本体收口验收报告：2026-09-07

> 本页保留前轮证据；当前状态见[续轮收口报告](ENGINE_CLOSURE_CONTINUATION_2026_09_07.md)。后续已实现 Subsystem 启动政策、启动失败 owner、Play 退休与跨 root 资产 IO 共享，整项未完成范围仍明确列出。

[架构索引](README.md) · [实施清单 C01–C18](ENGINE_CONSOLIDATION_IMPLEMENTATION.md) · [Overview](ENGINE_ARCHITECTURE_OVERVIEW.md) · [Identity / Recovery 标准](IDENTITY_REFERENCE_RELOAD_STANDARD.md)

## 验收结论

**本轮改动的本地构建与回归通过，但用户要求的“除跨平台外全部收口”尚未达成。**

Windows/跨平台验收按本次要求不计入工作范围；不能用这一排除项掩盖仍然缺失的本机架构实现。最重要的阻断项仍是 C07：`ReferenceRecoveryTransaction` 尚无完整生产调用链，Assets、Scene、Graph、Settings、Plugin、History 并未全部通过统一 Missing-slot 恢复事务发布。因此本报告不授权把本体标记为完整收口，也不建议据此冻结全部 Plugin 契约。

本轮未开发玩法 Plugin，未增加旧 API、旧格式兼容、migration 或 schema version；未提交或推送 Git。既有目录迁移和用户工作区改动保留。

## 已完成的实现

| 领域 | 实际源码变化 | 对应验证 |
| --- | --- | --- |
| Identity 解析 | Asset ID 缺失时不再通过 lastKnownPath 命中另一份资源；resolver bridge 再次核对 persistent ID | 错误 ID + 正确路径的负向回归 |
| Missing 数据 | 删除/退休 Asset 保留中立 state bytes、依赖与 last-good key；不恢复 live object，也不让 tombstone 成为可用源 | 资产删除/重命名/恢复相关回归 |
| 只读安装源 | Import failure 只更新 Catalog/诊断，不再次写只读 `.imeta`，避免二次异常掩盖原始导入失败 | 真实只读 metadata 不一致故障回归 |
| Recovery 补偿 | 失败 participant 本身也回滚；禁止重复 Activate；完成/失败后释放 participant 引用 | References 原子性与逆序补偿测试；不等于生产迁移完成 |
| 异步所有权 | `LifetimeScope.RunAsync` 原子登记、合并取消；`RetirementPendingException` 区分尚未退休与终态清理错误 | Core.Execution、Storage、Runtime retirement 回归 |
| Pending 传播 | Pipeline/Session/EngineHost 保留仍被任务使用的依赖；Session 退休后才注销；Shell 在 owner thread 重试产品退休，Editor 资源栈保留 pending 项 | 公开 factory → Session → Host 实例测试、真实 Editor/Player 退出 |
| Storage | FileSystem Dispose 不销毁仍有等待者的 Semaphore；Write 在原子提交前再次检查取消；Runtime 关闭先取消/等待 IO | 8 MiB Write/Dispose 竞态和可替换 Storage backend 的确定性测试 |
| Registry 所有权 | `CreateExtension` 自动登记候选新实例；自定义工厂使用 `OwnCandidateExtension`；Build 失败统一补偿，成功由 Snapshot 接管 | 新候选构造失败与补偿失败测试 |
| Registry 清理 | Importer、Build Processor、Property/Inspection Drawer、Asset Editor、Editor/Project Settings、Viewport Contributor 使用统一去重逆序释放；不再只调用诊断 hook | 完整回归、Architecture 禁止 diagnostic-only cleanup 绕过 |
| Provider generation | Audio/Render Provider 由 TypeCatalog Snapshot 独占；构造失败尝试全部候选清理；退休失败 Faulted，不伪装可恢复候选失败 | Audio 构造/退休失败、共享 gate Faulted、完整 Rendering 回归 |
| Scene 退休 | 旧 Scene element 清理失败聚合抛出，不再只记 Warning；GenerationCoordinator 的不可逆阶段负责 Fault | Scene/Editor/Scripting 回归；专门的跨域恢复验收仍未关闭 |
| Audio 内部 owner | `AudioClipCache` 实际拥有 Clip、Artifact、preload 与预算；`AudioMixerOwner` 实际拥有 Bus、控制状态和旧 graph | preload、Decode/Stream、取消、Mixer、清理失败测试 |
| Audio 设备替换 | 先完整准备候选 graph，再退休旧 backend；不可逆退休失败清理全部 owner、释放候选并 Fault；不会报告“旧设备仍然可用” | 旧设备 Dispose 故障、候选释放计数、禁止继续 Update |
| Audio 诊断/Bus | 恢复撤销 Missing/no-device/lost/recovery-failed；Provider 成功/移除撤销失败；旧 Bus 只等待实际使用它的 Voice | Mixer/设备恢复诊断回归；常规 Audio 回归 |
| Rendering | 候选失败与已发布后退休失败分开；清理尝试全部 generation/resource owner，保留 last-good 语义 | Rendering Runtime、Reload、MaterialGraph 回归 |
| 发布集合 | Graphics/Compute/Vertex Layout、Shader IR/编译结果/Artifact、Assembly catalog、Asset mounts 冻结；Graph/Material/Editor settings 集合不能强转改写内部容器 | 不可写 IList 负向验证、全仓编译与领域回归 |
| 冷资产 IO | 同一 persistent ID 的并发 Acquire 合并；单个取消不取消其他等待者；准备阶段 bytes 预算独立于驻留预算 | Build 的真实 Catalog/Artifact 冷载、取消、合并、超预算拒绝 |
| 队列 | Job 每帧 admission、主线程 callback capacity/drain budget；Audio preload waiter 与 Rendering request 上限；音频完成排空有界 | SingleThread/WorkerPool 压力边界、递归 callback 延后测试 |
| GC barrier | 12 轮真实 collectible module reload；static、event、Task、Thread、GCHandle、native callback delegate 保留根触发严格 barrier Fault | Modules 20 项测试及完整回归，不弱化 GC/weak monitor |
| 架构工具 | 新增 `tests/tooling` CLI 负向项目；global using、具体 backend、手拼 Asset resolver、Ma 类型、吞清理异常五类坏例 | 真实 Architecture CLI 执行，无 internal/test 后门 |

## 最终代码职责

```text
foundation
├── Core.Execution                 cancellation / tracked work / retirement signal
├── Extensibility.Types            candidate ownership / registry snapshots
└── Extensibility.Reload           shared publication gate / Fault / GC verification

content
├── Assets                         cold preparation / residency / artifact leases
├── Assets.Pipeline                authoring identity / readonly mounts / missing intent
├── References                     reference protocol and compensation primitive
└── Scene                          scene-owned state and retirement

services/audio/Inno.Audio.Runtime
├── AudioRuntime.cs                public service / Voice and content orchestration
├── AudioClipCache.cs              native clips / retained artifacts / preload budget
├── AudioMixerOwner.cs             bus graph / controls / old graph retirement
└── AudioExtensionRegistry.cs      TypeCatalog-owned provider generation

composition
├── Shell                          owner-thread product retirement before adapters
├── Player                         Session → settings → EngineHost teardown
└── Editor                         reversible resource stack / product lifecycle
```

上图是本轮实际职责，不是新建一套全局 Manager。Shell、Layer/LayerStack、RuntimeSubsystem 与领域 Feature 继续保持不同层次。没有把 Audio/Render 业务算法提升到 Core，也没有把 SDL/BGFX/MiniAudio 暴露给游戏 API。

## 新增/变更公开入口的必要性

| API | 稳定语义 |
| --- | --- |
| `RetirementPendingException` | 清理还不能完成；依赖必须保留，不能与一般 Dispose 故障混在一个 catch 中继续销毁 |
| `LifetimeScope.RunAsync<TResult>` | “检查仍可接收 → 启动 → 登记”原子化，关闭不能漏掉刚开始的 IO |
| `TypeRegistry.CreateExtension`（改为实例 protected） | 创建即归属当前候选；不保留 static 兼容入口 |
| `OwnCandidateExtension` / `DisposeExtensions` | 自定义工厂与普通工厂共用补偿和去重逆序退休；成功 Snapshot 仍明确负责释放 |
| `ReportRetirementFailure` | 领域持有的组合资源无法退休时进入同一个 Fault gate，不能另写 diagnostic sink 当失败处理 |
| `JobSchedulerOptions.maxFrameJobs/mainThreadCapacity/mainThreadDrainBudget` | 明确 admission 与单次执行工作量，不静默丢任务 |
| `AssetDatabase.preparationBudgetBytes/preparingBytes` | 构造预算和准备中的 byte reservation 可见，与 resident payload 预算分离 |
| `RuntimeSessionOptions.assetPreparationBudgetBytes` | Session/Player 能实际配置并传递 cold preparation 预算，默认 64 MiB |
| `AudioRuntimeOptions.maxPendingPreloads` | 限制 pending waiter 数量，默认 256；超限明确拒绝 |
| `AudioRuntime.ReplaceDevice` | 消费候选；准备失败清理候选，退休失败则 Fault，不能虚假回滚 |

新增 Audio owner 均为 internal；没有向脚本暴露设备、缓存管理类、Registry 细节或任何 Ma/BGFX/SDL 类型。

## 本地验收证据

| 验证 | 本轮结果/证据 |
| --- | --- |
| Debug Solution build | `/tmp/inno-final-closure-build-12.log`：0 warning / 0 error |
| 完整测试 | `/tmp/inno-final-closure-tests-3.log`：48 个测试项目，742 passed / 0 failed / 0 skipped |
| Architecture | `/tmp/inno-final-closure-architecture-2.log`：validation passed |
| 历史 reentrant baseline failure | `EditorHistoryTests.CompensationFailureFaultsHistoryAndRejectsFurtherTransactions` 已修正 Catalog 启动重入，进入本轮全绿结果；不再作为基线失败豁免 |
| GC 专项 | `/tmp/inno-final-closure-unload.log`；相同测试也包含于最终完整测试 |
| 首轮 Support Pack / Player | `/tmp/inno-final-closure-support.log`、`/tmp/inno-final-closure-player.log` 成功；最终产物复验另见下节 |
| 空项目 Editor | `/tmp/inno-final-closure-editor.log`：30 帧、保存 editor state、完整 Dispose、BGFX shutdown；最终产物复验另见下节 |
| 源码卫生 | `git diff --check` 通过；未使用 IVT、反射穿透或扩大 internal 可见性来测试 |

这些是具体已运行的证明，不意味着任意第三方 Plugin、无限时长压力、可听音效或所有跨域失败注入都已经验收。Native tests 使用 no-device 能证明 ABI/资源流程，不能证明实际扬声器有声。测试期间只使用新建临时 Project，不向 TestProject 安装插件或覆盖 Rendering2D 创作数据。

## 尚未完成：仍然阻止整体收口

| ID | 剩余范围 | 必须补齐的关闭条件 |
| --- | --- | --- |
| C07 | Assets/Scene/Graph/Settings/Plugins/History 全部 Missing-slot 的生产 Recovery 统一 | 同一候选批次解析和验证；失败恢复所有 owner；保留原 persistent ID、type ID、结构、bytes 和 Undo 栈；生产路径不能只存在不同的领域恢复协议。当前 `src` 中尚无 `new ReferenceRecoveryTransaction` 的完整接入 |
| C04/C08 | 初始化失败发生 Pending 时，全部外层 owner 的责任与终态一致性 | Factory/Attach 失败不能丢失仍需排空的 pipeline；Editor Play entry/exit 全链故障注入；超时必须保留 owner 并明确 Fault；不能仅依赖正常 Dispose 回归 |
| C05 | 全部公开发布面的嵌套可变对象审计 | 已修复容器不等于 Shader definition、Material、Scene presentation 等深层图都不可变；创作对象与运行快照必须逐类型界定并验证 |
| C09 | 领域 owner 拆分尚未全部完成 | Audio Voice scheduler/content recovery、Rendering 大型资源负责人仍需继续按实际所有权拆分；不能把本轮两个 Audio owner 说成全仓拆分已完成 |
| C06 | 全部生产扩展资源与跨域恢复失败覆盖 | 已补通用 Registry 和保留根矩阵，但不能据此声称每个自定义 factory、Task/native callback 退出路径和恢复事务均已验证 |
| C13/C14 | 完整压力及统一观测 | 不同 root 共享依赖的冷读合并、长时 cache/retirement 压力；各队列 current/high-water/rejected 与 bytes/retirements 指标；剩余领域容器 admission 审计 |
| Subsystem descriptor | required/optional、capability 和失败政策仍非完整实现 | 当前 Descriptor 只有 ID/order/dependencies/lifetime；必须实现验证/降级政策与测试，不能只在 Overview 中列成已有 API |
| C17 | 完整架构负向矩阵 | 当前五类 CLI fixture 不是全部符号/ImGui/动态 callback 边界证明；规则必须结合可执行失败测试 |
| C18（本地） | 真实 Editor Play → Stop → Reload → Play 纵向专项 | 本轮空项目 smoke、完整测试和 Player 发布不能替代该含真实跨域恢复/History/可视内容的重复工作流 |
| C01 | 全量 Wiki 历史示例/深层签名仍需审计 | 本轮相关页面已更新；发现并修正 Types 静态旧示例、Graph JSON 描述、Descriptor 虚报能力，但不宣称所有历史页面均重新核实 |

以上是明确未关闭的需求，不是默认批准延期，也不是跨平台排除项。下一次继续实施应直接使用这些停止条件，不再重新搭一层平行框架；完成前不得把报告改成“全部已完成”。

## 最终产物复验

- `/tmp/inno-final-closure-support-final.log`：包含最终源码的 macOS ARM64 Release Support Pack 成功生成，位于 `/tmp/inno-final-closure-validation.CJ4kvC/support-final/macos-arm64`。
- `/tmp/inno-final-closure-player-final.log`：从最终 Support Pack 实际导出并启动 Player，Metal/BGFX 初始化、帧运行、关闭成功，`Player E2E passed`，产物 `/tmp/inno-final-closure-validation.CJ4kvC/player-final/Builds/InnoPlayerE2E.app`。
- Support Pack 原生闭包包含唯一的 `native/miniaudio/macos-arm64/libminiaudio-release.dylib`；E2E 同时检查 runtime-only managed closure。三帧 smoke 不代表视觉小说/2D Plugin 效果验收。
- `/tmp/inno-final-closure-editor-final.log`：最终 Debug Editor 在临时空 Project 运行 30 帧，保存 editor state、退出 Run loop、完成 Dispose/BGFX shutdown，进程退出码 0。
- 非历史 Wiki 扫描 661 个本地文件链接，0 断链；`git diff --check` 再次通过；最终 Architecture 再次通过。
- 完成提示音执行 `afplay /System/Library/Sounds/Glass.aiff`，退出码 0、无错误输出；本轮播放命令成功完成。不能据此替代游戏音频输出验收。
