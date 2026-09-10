# 本体收口：实现交付与集中验收（2026-09-08）

[架构索引](README.md) · [Overview](ENGINE_ARCHITECTURE_OVERVIEW.md) · [完整清单](ENGINE_CONSOLIDATION_IMPLEMENTATION.md) · [Identity 标准](IDENTITY_REFERENCE_RELOAD_STANDARD.md) · [之前的执行证据](ENGINE_CLOSURE_CONTINUATION_2026_09_07.md)

## 范围与阶段

本次按用户要求先完成全部剩余实现、生产调用方、测试场景代码与当前 Wiki，再开始集中验证。实现阶段结束不等于验收通过。本页是当前状态入口；旧报告中的“未关闭”是当时的快照，不继续作为当前清单。

- ImGui 的原生 flags、显式 scripting exports 和外部 Editor UI 直接调用是既定设计，保持不变。SDL3/BGFX/MiniAudio 仍只由对应 Adapter 实现暴露为中立边界。
- 不实现实际玩法 Plugin、Physics，不修改 TestProject/Rendering2D 源项目；Windows 实机原生验收按用户范围排除。
- 996 passed / 0 failed / 0 skipped（48 项目）是本次之前的基线，不能当作下面新增代码的测试结果。
- 每 30 分钟续跑已暂停，不重新创建自动化。

当前结论：清单内源码、调用方、测试场景和文档实现已完成，已在开始验证前明确通知用户。最新完整自动验收通过：48 个测试项目、1021 passed、0 failed、0 skipped，Solution 零警告/错误，Architecture 与非空原生 Editor 启停通过；Release Support Pack/Player E2E 是前一批发行验收证据，本次 Editor/GC 修复未重新打发行包。**不能宣称无保留的整体验收关闭**：GUI 交互缺少 Computer Use 权限；中途一次 Plugin 移除测试失败尚未复现、原因未确认，见下方验收保留项。后续绿灯不抹去此记录。

## C01–C18 实现状态

| 清单 | 实现结果 | 当前验收 |
| --- | --- | --- |
| C01 标准/Wiki | 修正当前事务签名、Graph public/internal 边界、Subsystem/Layer/Feature 术语、预算/快照/退休契约及索引；旧证据明确为历史 | 139 个非历史页面本地链接检查通过；源码 XML 与 Architecture 通过，不声称每个历史示例均已独立编译 |
| C02 Execution | 共用 ExecutionSlot/LifetimeScope；有限 work admission、成功/取消 Task 即时脱离 owner、无 ExecutionContext 捕获的完成回调 | Execution 28 项通过，包含 256 次容量回收及结果弱引用测试 |
| C03 Diagnostics | 延续 Core DiagnosticHub/Reporter/Sink 唯一问题所有权，不新增领域 sink | Diagnostics 10 项、Logging 6 项通过 |
| C04 退出所有权 | 延续 Layer/Module/Panel/Registry/Runtime/Shell 的 Core Pending/timeout；ScriptReloadHost 关闭前等待上一代 GC 屏障；Storage 先排空工作再释放 backend | Core/Runtime/PlayMode 回归、独立脚本关闭回归与原生 Editor 600 帧退出通过 |
| C05 发布快照 | Plugin manifest/scan/catalog、Shader diagnostics/pass definition、Geometry collections、Settings contributors/effective records 由发布方持有隔离副本 | Plugin、MaterialGraph、Rendering 与 Settings 嵌套值隔离/失败回滚测试通过 |
| C06 代际与工作 | Plugin background scan 进入 LifetimeScope；Editor Capture 进入共享 Prepare；失败准备只恢复已尝试 participant；强引用根矩阵、真实循环重载与关闭屏障实现 | Modules 31 项、Reload 22 项及脚本回归最新通过；Plugin 单次失败信号继续保留调查 |
| C07 Recovery | Graph 文档 Identity session + Missing History；Settings exact neutral rollback；Plugin availability/content/script publication 接入共享五阶段 transaction | Graph 6 项含真实 assembly 发布/后续 participant 拒绝；References 22 项、Scene 49 项通过；Plugin 保留项见下文 |
| C08 Subsystem | 统一 Contracts、生命周期模板、DAG、Host/Session 范围，保持通用 Shell 和 composition 边界 | Runtime 26 项、PlayMode 21 项、Layer 10 项通过 |
| C09 领域 Runtime | Rendering 拆分 Geometry/Material/Readback/ResourceCache owners，共同执行创建、替换、退休；Audio/Input/Storage/Animation 延续统一 RuntimeSubsystem 骨架 | Rendering Runtime 46 项、Audio Runtime 46 项和其他领域回归通过 |
| C10 内容输入 | 延续 Identity-backed ContentReadScope，不恢复 Scene-specific Audio/Rendering 桥 | References、Rendering、Audio、Scene 回归通过 |
| C11 Animation | 中立 target/binding、采样/混合/marker、Provider；播放容量与 stale slot 保护 | Animation 7 项通过，包含连续 256 次容量复用 |
| C12 默认装配 | 项目本地声明 + 强类型生成器、Engine.Default/Adapter.Default；Host 无领域中央名单 | 生成器 8 项、脚本编译与 IDE 裁剪引用测试通过 |
| C13 Asset/Audio async | 共享 residency、ArtifactLease、budget eviction 与取消；Audio native/muted 有限 Clip/Bus/Voice + completion admission | Assets 29 项、Pipeline 90 项、MiniAudio native 2 项、adapter 5 项与 Audio 回归通过 |
| C14 队列/预算 | Jobs 原子 ParallelFor admission、有界 callback 排空及 Pending/stats；Rendering/Storage/Animation/Audio owner 独立容量与拒绝契约 | Jobs 28 项、Storage 7 项及各领域容量/循环压力测试通过 |
| C15 命名/封装 | 统一 owner/Subsystem/Reporter；新公开类型仅用于中立预算/统计或正式 recovery boundary，无 legacy overload/alias | Solution 零警告/错误；公开符号/引用检查通过 |
| C16 Solution/Scripting | 真实目录与 Solution、fixture 归类；不扩大 gameplay scripting exports；ImGui exports 原样保留 | Solution/Architecture 通过；Editor Scripting 最新 62 项通过；首轮额外两轮各 60 项通过 |
| C17 Architecture | 符号正反例、依赖与目录/公开面约束；ImGui 为用户确认的设计例外 | Architecture CLI 通过，47 项工具测试通过；ImGui ScriptingApi.cs 无 diff |
| C18 验收场景 | 真实脚本/Plugin/非空 Scene 的 Play→Stop→Reload→Play 循环，以及 owner/资源压力测试；本机发布和原生运行路径 | 最新全量 1021 项通过；首轮 Support Pack/Player 通过，本轮非空 Editor 复验通过；GUI 权限与未解释的单次失败使无保留验收仍未关闭 |

## 统一 OOP 结构

`RuntimeSubsystem` 只承担有限帧阶段、scope 和 lifetime；每个领域组合自己的算法 owner，不能把 Core Layer、领域 Feature 与 Subsystem 混成一个继承树。每种 owner 明确接纳、发布、退休三个边界，Pending 保留步骤，普通错误聚合，终止 timeout 封锁共享 generation。

`IGenerationChange` 是唯一五阶段领域变化协议；`ReferenceRecoveryTransaction` 组合可解析引用 slots 与真实领域 participant。Settings 位于 Foundation，只依赖 Foundation Reload；Plugin/Graph 上层负责组合，不制造反向引用。Plugin ID、Setting ID、Shader Node Stable ID 是语义，不伪造 object Guid；真正的 live Graph document session 和 Asset/Scene 对象才通过 IdentityAllocator 定位。

`IScriptReloadCoordinator.Execute(reload, externalChange)` 以受拥有的 change 替换两段 Action。Prepare 在共享 gate 中捕获；候选 assembly 发布后先应用外部内容再应用 Editor 状态；失败先恢复结构，再恢复旧 assembly/content resolver，最后恢复旧属性。Complete 后只允许退休，不能伪回滚。

## 关键新增公开 API 的必要性

| API | 必要性与稳定语义 |
| --- | --- |
| LifetimeScope capacity/statistics | 防止后台任务无限积累；RunAsync 在执行前接纳，失败任务保留至报告 |
| JobSchedulerStatistics | Host 查看队列/帧峰值、拒绝与取消；没有 callback/Type/native 对象 |
| ProjectSettingsStore.CreateReloadChange / unavailableSettings | Foundation 中立恢复边界与 Missing 记录查询，不导出游戏脚本 |
| PluginEnvironment.CreateReloadChange / ApplyPendingChange | code/content 两种安装变化共用代际 gate，不再手写领域提交顺序 |
| GraphEditorModule Guid 文档 API / controller.isAvailable | 当前 Identity domain 解析、同 persistent ID 跨代恢复、旧 controller 失效 |
| RenderResourceLimits / RenderResourceStatistics | Host 预算与可观察接纳结果；不暴露 BGFX |
| AudioDeviceLimits | 独立于 gameplay stealing 的 backend 硬容量；保持中立，不导出脚本 |
| StorageRuntime admission counters / AnimationRuntime playback counters | 有界工作与拒绝计数；不直接暴露内部队列 |

默认容量：Lifetime 4096；Jobs frame/callback 65536、一次 callback drain 4096；Graph documents 128；Rendering 每类资源16384、readback64、upload页1024/256MiB/每帧64MiB、target1024；Storage pending128/单次写64MiB；Animation playback16384；Audio device Clip16384/Voice+completion65536/Bus4096。各参数只在其真实 owner 初始化边界配置，不增加万能 settings 大对象。

## 关键文件结构

```text
src/foundation/core/
  Inno.Core.Execution/LifetimeScope.cs
  Inno.Core.Jobs/{JobSchedulerStatistics.cs,Systems/*JobSystem.cs,Systems/MainThreadWorkQueue.cs}
  Inno.Core.Settings/{ProjectSettings.cs,ProjectSettingsStore.cs}
src/foundation/extensibility/Inno.Extensibility.Reload/GenerationCoordinator.cs
src/runtime/plugins/Inno.Plugins.Authoring/{PluginEnvironment.cs,PluginRecoveryParticipant.cs}
src/runtime/scripting/Inno.Scripting.Reload/{IScriptReloadCoordinator.cs,ScriptReloadHost.cs}
src/services/rendering/Inno.Rendering.Runtime/
  RenderResourceService.cs
  RenderResourceCache.cs
  RenderGeometryOwner.cs
  RenderMaterialOwner.cs
  RenderReadbackOwner.cs
  RenderResourceLimits.cs
  RenderResourceStatistics.cs
  RenderFrameUploadService.cs
  RenderTargetStore.cs
src/composition/editor/framework/
  Inno.Editor.Core/Reloading/EditorReloadCoordinator.cs
  Inno.Editor.Graph/{GraphEditorModule.cs,GraphDocumentSession.cs,GraphDocumentController.cs,GraphHistory.cs}
src/composition/editor/panels/Inno.Editor.Panel.MaterialGraph/MaterialGraphPanel.cs
```

不新增占位项目、测试后门、兼容 namespace、迁移 reader、托管实时 DSP callback 或另一个诊断 owner。

## 验收与可信范围

本节原始完整验收为 1019 项；同日用户反馈自动编译弹窗循环后，追加修复与新测试证据见文末。测试全绿不替代未覆盖的真实自动模式，前次大多数脚本 fixture 使用 autoCompile=false，因此没有发现通知回声。

实现代码和测试代码先完成，再集中验证。以下为最后源码状态的真实证据；临时日志/项目不属于发行包，可能被操作系统清理，复现命令与结果保留在本页。

| 检查 | 结果与证据 |
| --- | --- |
| Solution build | 通过，0 warnings / 0 errors；`/tmp/inno-closure-build-6.log` |
| 完整自动测试 | 48 个项目，1019 passed / 0 failed / 0 skipped / 0 aborted，进程 exit 0；`/tmp/inno-closure-tests-acceptance.log`，48 个 TRX 位于 `/tmp/inno-closure-final.npei9S/acceptance-tests` |
| Architecture | 通过，进程 exit 0；`/tmp/inno-closure-architecture-verified.log` |
| Release Support Pack | 通过；`/tmp/inno-closure-support-final.log`；`support-final/macos-arm64` 中恰好一个 MiniAudio Release dylib，没有 authoring ScriptReload 程序集 |
| Player E2E | 导出、runtime closure 校验、Metal 原生启动/3 帧/退出通过；`/tmp/inno-closure-player-final.log`；最终应用位于 `/tmp/inno-closure-final.npei9S/player-final/Builds/InnoPlayerE2E.app` |
| 非空原生 Editor | Rendering2D 的 Assets/Settings/editor.ini 临时副本，加载 SampleScene、实际创建 Sprite shader/geometry，600 帧后保存状态并正常退出；`/tmp/inno-closure-editor-verified.log`，进程 exit 0；未修改原 Rendering2D 项目 |
| 生命周期循环 | 自动化测试含 8 轮非空 Scene Play/60 ticks/Stop/Plugin reload/GC，再次 Play；还含真实 collectible roots、连续重载和资源 256 次容量循环；不将这些自动化测试称为 GUI 点击验收 |
| 文档/差异 | 139 个当前非历史页面无损坏本地链接；`git diff --check` 通过；ImGui `Properties/ScriptingApi.cs` 未修改 |

首轮验收最后的源码修复仅涉及 authoring ScriptReloadHost 关闭等待及其回归，故当时的 Support Pack/Player 证据有效。文末追加包含 Core.Execution 的进一步修复，不把首轮发行包当作追加源码的重新发行验收。

### 验证中发现并修复的问题

- 补齐 PluginEnvironment 构造边界的 Build CLI 调用方；修正公开 XML 和 nullable 标注，最终 Solution 零警告/错误。
- Graph IdentityAllocator 是弱索引，不能独自保活文档：Module 增加独立 session lifetime 所有权，所有寻址仍经过 Identity；旧 controller 不重新认领同 ID 的新 session。
- MaterialGraphPanel 不向注入器请求存在多个候选的 IdentityAllocator，明确使用 AssetPipeline 的 authoring domain。
- Lifetime 成功/取消 work 在锁内发布完成并移出 owner；完成统计不受延迟 continuation 干扰，也不长期保留 TResult。
- Storage 的 backend 改由 operation scope 拥有，关闭先排空操作，再释放 backend。
- Readback 取消遵守控制线程安全点先清理 native staging 再完成 Task；测试推进对应 frame 后验证取消，没有取消后提前丢 native owner。
- ScriptReloadHost 在关闭前等待已有 AwaitingCollection。新增 `ShutdownWaitsForPreviousRetirementBeforeUnloadingActiveScripts` 通过；原先失败的非空原生 Editor 600 帧退出已重跑通过。

以上修复不是“基线失败豁免”。失败日志保留，最终证据使用修复后的二进制。

### 验收保留项（不是新增功能规划）

1. **GUI 交互验收未执行完成。** Computer Use 返回 `Computer Use permissions are not granted`。没有通过其他通道绕过权限；也没有把命令行启动或 headless Play 循环冒充人工点击 Play/Stop、编辑并观察 Reload 的可视结果。需要授权或用户实际操作后才能关闭此项。
2. **Plugin 移除曾出现一次尚未定位的间歇性失败。** `/tmp/inno-closure-tests-verified.log` 记录 `PluginRemovalUnloadsTheCommittedMissingGenerationWithoutASecondReload` 失败一次；该轮简略 logger 未保留错误堆栈，不能推测为环境或基线原因。未改该测试断言来掩盖失败。随后完整脚本测试 60/60、该项独立 12 次、完整 solution 1019/1019、另外两轮脚本各 60/60 均通过。详细复验见 `/tmp/inno-closure-scripting-recheck.log`、`/tmp/inno-closure-plugin-removal-{1..12}.log`、`/tmp/inno-closure-scripting-stress-a.log`、`/tmp/inno-closure-scripting-stress-b.log`。未复现不等于根因已修复，不排除实现缺陷；此信号继续保留，不能宣称零已知验收风险。
3. Windows x64/其他跨平台实机验收按本次范围排除，不以 macOS 通过替代。

### 复现入口

在仓库根目录使用本机 .NET 9 SDK：

```sh
dotnet build InnoEngine.sln --no-restore -m:1 -p:UseSharedCompilation=false -p:NuGetAudit=false
dotnet test InnoEngine.sln --no-build --no-restore -m:1 --logger 'console;verbosity=normal' --logger trx --blame-hang-timeout 90s
dotnet tools/Inno.Tooling.Architecture/bin/Debug/net9.0/Inno.Tooling.Architecture.dll
```

原生 Editor smoke 使用正常 CLI `<project-path> --smoke-frames 600`；此参数不执行 GUI Play 点击。未全局安装 .NET runtime 的环境需为 apphost 指定正确 `DOTNET_ROOT`。

完成提示音：沙箱内 `afplay` 返回 `AudioQueueStart failed (-66680)`；经权限流程在沙箱外重试 `/System/Library/Sounds/Glass.aiff` 后退出码为 0。

## 同日追加：自动编译与卸载验证循环

用户反馈 80% 的 Script compilation completed 与 Verifying retired generation unload 反复出现。根因是脚本发布参与者重新发布 Asset Catalog 时产生 SourceMountsChanged，被开启 autoCompile 的 ScriptReloadHost 当作新输入再次排队。它是反馈循环，不应通过减少 GC 验证强度或清理 Project 数据处理。

修复内容：

- 只在本 ScriptReloadHost 同步发布候选期间忽略自己产生的 mount 通知回声；外部 mount 发布、真实源文件变化和显式编译请求继续生效，File Browser 仍收到完整源代际通知。
- Editor 票据和 IDE 投影延后至退休验证完成；即使其他 Host API 先推进了屏障、或者窗口失焦，Editor 仍完成 deferred ticket 并关闭进度弹窗。
- Faulted 优先于排队和忙碌标记；失败发布诊断、票据明确失败、进度弹窗关闭，Play/Build/reload 门禁继续锁定。没有重置 barrier 或遗忘 monitor。
- ImGui flags、公开 UI API、原 Rendering2D Project 均未修改。

新增自动模式回归：`AutomaticPublicationDoesNotQueueItselfButExternalMountChangesStillDo` 与 `EditorAutomaticCompilationClosesModalAndTicketWaitsForRetirement`。后者实际推进 Editor module、读取正式 modal presentation，保持旧 Type 强引用时断言票据不成功，释放后由其他 Host 边界先完成 GC，再在失焦 Editor Update 中完成票据并隐藏弹窗；不通过反射读取 internal Editor 状态。

初始两项回归通过，日志 `/tmp/inno-reload-loop-regression-2.log`；临时 Rendering2D 副本原生 Editor 600 帧正常退出，日志 `/tmp/inno-reload-loop-editor.log`。最终全量结果见下表，不以早先 1019 项计数替代本次新代码验证。

追加全量回归发现 `IdleLifetimeDoesNotRetainSuccessfulOperationResults` 失败，堆栈见 `/tmp/inno-reload-loop-tests-final.log`。已定位为 RunAsync 自有 TCS 同时注册 Track 完成观察和同步移除，RunContinuationsAsynchronously 导致无必要的观察回调持有 completed Task/TResult。改为共同接纳逻辑：外部 Track 保留观察，自有 RunAsync 在自身完成路径直接释放，不修改或放宽原弱引用断言，也不降低 GC 验证标准。

“完成”只针对上述有限清单与可执行契约，不是保证任意第三方代码无泄漏。无限阻塞的托管 callback 不可安全抢占；恶意 unsafe/MemoryMarshal 也不是只读集合的安全沙箱。Faulted Host 必须重启，不能把释放所有权丢掉来强行通过 GC。Windows 实机验收不以 YAML 或本机模拟替代。

### 追加修复的最终验收

| 检查 | 结果 |
| --- | --- |
| 完整 Solution build | 0 warnings / 0 errors；`/tmp/inno-reload-loop-build-complete.log` |
| 完整自动测试 | 48 项目，1021 passed / 0 failed / 0 skipped / 0 aborted，exit 0；`/tmp/inno-reload-loop-tests-verified.log`；TRX 位于 `/tmp/inno-reload-loop-verified-results` |
| 任务结果回收 | 保留原弱引用断言，最终二进制连续 20 轮通过；`/tmp/inno-reload-lifetime-complete-{1..20}.log` |
| Architecture | 通过；`/tmp/inno-reload-loop-architecture-verified.log` |
| 非空原生 Editor | 最终二进制运行 600 帧、保存状态、正常退出，exit 0；`/tmp/inno-reload-loop-editor-verified.log` |
| 文档与差异 | 139 个当前非历史页面本地链接有效，git diff --check 通过 |

最终测试、连续复验和原生启停均使用完整编译后不再修改的二进制；编译进行中的中间运行不作为最终证据。本次没有删除 Project/Library，也没有修改原 Rendering2D 或 ImGui 公开导出。实际 GUI 点击与 Windows 验收范围仍按前述保留项处理；本次自动模式回归验证的是正式 Editor 模块和 modal presentation 状态。
