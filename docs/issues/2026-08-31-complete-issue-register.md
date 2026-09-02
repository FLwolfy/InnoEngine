# 全量问题台账与关闭证据

[Issues 索引](README.md) · [原始完整审查](2026-08-31-full-architecture-audit.md) · [整改总方案](2026-08-31-architecture-remediation-master-plan.md)

本文是架构问题状态的唯一事实来源。历史证据描述发现问题时的实现；“当前实现”只描述本次硬切后的源码。旧 API、旧格式和旧项目不构成兼容承诺。

## 状态定义

| 状态 | 含义 |
| --- | --- |
| 已关闭 | 实现、调用方、测试和架构守卫均满足关闭标准 |
| 待平台验证 | 实现与自动化入口完成，但当前主机不能执行目标平台进程 |
| 已确认 | 仍缺少实现或可执行证据，不得视为完成 |

## 完整索引

| ID | 优先级 | 状态 | 最终 owner | 整改阶段 |
| --- | --- | --- | --- | --- |
| ARCH-001 | P0 | 已关闭 | Rendering.Assets / Build / Runtime | 5、6 |
| ARCH-002 | P0 | 已关闭 | Assets.Pipeline / Build | 3、6 |
| ARCH-003 | P0 | 已关闭 | Core.Serialization.Generators | 1 |
| ARCH-004 | P0 | 已关闭 | Build.SupportPacks | 6 |
| ARCH-005 | P1 | 已关闭 | Runtime / Build targets | 6 |
| ARCH-006 | P0 | 已关闭 | Editor.PlayMode / Runtime | 4、6 |
| ARCH-007 | P1 | 已关闭 | Scene / Rendering.Scene | 4、5 |
| ARCH-008 | P1 | 已关闭 | Editor.Diagnostics | 4 |
| ARCH-009 | P1 | 已关闭 | Editor.Diagnostics / Logging Panel | 4、7 |
| ARCH-010 | P0 | 已关闭 | Build / SupportPacks | 6 |
| ARCH-011 | P0 | 已关闭 | Build / Player | 6 |
| ARCH-012 | P0 | 已关闭 | Build / Assets / Serialization | 6 |
| ARCH-013 | P1 | 已关闭 | Build / Editor.Exporting | 6、7 |
| ARCH-014 | P1 | 已关闭 | Build | 6 |
| ARCH-015 | P1 | 已关闭 | Plugins.Authoring / Build | 3、6 |
| ARCH-016 | P1 | 已关闭 | Build / Plugins.Authoring | 3、6 |
| ARCH-017 | P1 | 已关闭 | Plugins.Authoring | 3 |
| ARCH-018 | P0 | 已关闭 | 各真实 owner / Architecture tooling | 1—8 |
| ARCH-019 | P1 | 已关闭 | Runtime / Core instance services | 1、6 |
| ARCH-020 | P1 | 已关闭 | Core.Serialization | 1 |
| ARCH-021 | P1 | 已关闭 | Runtime / Assets / Scripting bounded contexts | 1、2、3、6 |
| ARCH-022 | P1 | 已关闭 | Diagnostics / Logging / Extensibility | 1、2 |
| ARCH-023 | P0 | 已关闭 | Scripting.Compiler / Reload | 2 |
| ARCH-024 | P1 | 已关闭 | Editor.PlayMode | 4 |
| ARCH-025 | P0 | 已关闭 | Tooling.Architecture / CI | 1、8 |
| ARCH-026 | P0 | 已关闭 | Build / Scripting / Editor.Diagnostics | 2、6、7 |
| ARCH-027 | P1 | 已关闭 | Runtime | 6、8 |
| ARCH-028 | P1 | 已关闭 | Platform / Platform.Sdl3 | 5 |
| ARCH-029 | P0 | 已关闭 | Rendering | 5、8 |
| ARCH-030 | P0 | 待平台验证 | Tests / CI | 8 |
| ARCH-031 | P0 | 已关闭 | 全仓 | 1—8 |
| ARCH-032 | P0 | 已关闭 | Tooling.Architecture | 8 |
| ARCH-033 | P0 | 已关闭 | 全仓 / Tooling.Architecture | 1—8 |
| ARCH-034 | P0 | 已关闭 | Extensibility.Types / Modules | 2 |

## 逐项证据、根因与关闭标准

### ARCH-001：Runtime Texture 读取不存在的 Source Mount

- 历史证据：Player 纹理预热曾通过 `AssetSourceMount.Resolve` 重新访问创作源，而 Export 只部署 Artifact。
- 根因与影响：运行时和 authoring 编译职责没有切断；带纹理的 Player 在脱离项目目录后会失败。
- 当前实现：平台 Build Target 通过 `BgfxGameContentCompiler` 离线生成 `TargetArtifacts/Textures/*.ktx`；`FileRenderTargetArtifactProvider` 只读取已物化 Content，不存在 Source Mount fallback。
- 测试：`RuntimeSessionTests.TextureTargetArtifactLoadsWithoutAnyAuthoringSourceMount`、`BuildPipelineTests.GameBuildPublishesOnlyVerifiedContentPacksAndRuntimeAssemblies`。
- 关闭标准：Player runtime 路径不包含 Source Mount 或 texturec，目标纹理 Artifact 缺失时明确失败。已满足。

### ARCH-002：Runtime Artifact Bundle 不完整时错误出现过晚

- 历史证据：损坏或缺少 bundle manifest 会被当作普通 miss，直到 Player 加载阶段才暴露。
- 根因与影响：Artifact Store 捕获范围过宽，Build 没有严格区分“不存在”和“当前格式损坏”。
- 当前实现：`AssetArtifactStore` 对当前格式 manifest 做 key、output、hash、length 完整校验；损坏抛出带 bundle identity 的 `InvalidDataException`，Build 在 staging 阶段停止。
- 测试：`BuildPipelineTests.BackgroundArtifactExportRejectsACorruptCurrentFormatManifest`、编译失败与取消均验证无半成品。
- 关闭标准：损坏 bundle 不能进入 Content Pack，错误发生在原子提交前。已满足。

### ARCH-003：Manifest 被强制要求手写 Converter

- 历史证据：普通封闭 DTO 嵌套在集合时也必须编写重复 `SerializationConverter<T>`。
- 根因与影响：序列化值管线无法在编译期生成封闭对象协议，错误发生过晚且维护成本高。
- 当前实现：`Inno.Core.Serialization.Generators` 通过 `GenerateSerializationConverterAttribute` 生成 converter，并验证 key、构造器和支持类型；internal DTO 保持 internal。
- 测试：`BuildProfileStoreRoundTripsGeneratedCurrentFormat`、Editor Settings current-format round-trip 与 `SerializeDeserialize_RoundTripsSupportedDefaultValues`；这些类型的 converter 均由 generator 参与实际编译和运行。
- 关闭标准：普通 DTO 不再手写 converter，特殊多态/身份语义仍显式实现。已满足。

### ARCH-004：Export 从错误工作目录启动 dotnet

- 历史证据：旧 Publisher 在 Editor 导出时从引擎 checkout 推导 `.csproj` 并启动 `dotnet publish`。
- 根因与影响：导出依赖当前工作目录、SDK 和源码仓库，发布版 Editor 无法可靠使用。
- 当前实现：Game Export 只消费按 RID 预生成、验证过的 Player Support Pack；`dotnet` 只属于 Support Pack 生产工具，不在导出流程。
- 测试：真实 macOS Player E2E 从 Support Pack 组合并运行；`PlayerSupportPackCatalog` 拒绝 build-time closure。
- 关闭标准：`BuildPipeline.BuildGameAsync` 无 dotnet 进程和源码定位逻辑。已满足。

### ARCH-005：Persistent Data 未严格使用 Application ID

- 历史证据：旧 Player 以可执行文件名决定持久目录。
- 根因与影响：产品改名会改变存档位置，同名产品会冲突。
- 当前实现：`GameRuntimeManifest.applicationId` 先经过严格验证，再由 Player 解析持久根；`RuntimeSessionOptions` 明确携带 application identity 与目录。
- 测试：Build 测试解码 runtime manifest 并验证物化目录位于 application ID 根；Runtime Session 测试使用隔离 application ID。
- 关闭标准：进程名不参与数据身份。已满足。

### ARCH-006：Play Mode 的 Edit Scene 隔离不足

- 历史证据：旧流程依赖进入前快照和退出恢复，Edit 对象仍可能被运行逻辑触碰。
- 根因与影响：恢复不是隔离；异常、外部引用和未覆盖状态可能污染编辑内容。
- 当前实现：Play 创建独立 `RuntimeSession`、`SceneWorld` 与对象图；Edit Scene 只作为不可变启动快照输入，退出直接 Dispose Play Session。完整候选 world 成功后，Game View、Scene View、Hierarchy、Inspector、Selection 与 Gizmo 在同一安全点切换到 Play Scene；Play workspace 禁止持久化并使用独立 History 分支，退出后共同恢复 Edit presentation。Editor RenderRuntime 不再隐式把 Edit Scene 提交到 backbuffer。
- 测试：`EditorPlayModeTests.EntryWaitsForCompilationAndExitRestoresEditingHistory`、`CompilationFailureReturnsToEditWithoutReplacingScenes`、`SceneSessionRestoresGraphIdentitySelectionAndEditValues`、`GamePresentationSwitchesFromEditToPlayAndBackWithoutSharingObjects`、`RejectedPlayWorldDoesNotReplaceTheEditPresentation`。
- 关闭标准：Play 生命周期不在 Edit Scene 对象上运行；Game View 读取 Play Scene 的逐帧变化；失败、取消和退出不会替换或恢复 Edit Scene。已满足。

### ARCH-007：Renderer 与脚本组件的 enabled 语义不统一

- 历史证据：`GameBehavior` 有 enabled 生命周期，而 `SpriteRenderer2D` 等渲染组件独立实现开关。
- 根因与影响：Inspector 表现和 Scene 生命周期不一致，组件类型增加后产生重复逻辑。
- 当前实现：公开继承链收敛为 `GameComponent -> GameBehavior`；Project Script、Renderer、Camera、Light 都直接继承同一个 `GameBehavior`，统一 `enabled`、`isActiveAndEnabled` 与完整 Awake/Start/Enable/Update/Destroy 生命周期。独立 `Behavior` 类型、脚本导出和内部影子 activation interface 均已删除。`GameSystem` 本来就是单层类型，并与 `GameBehavior` 共用一个 internal Scene lifecycle 协议；两者的 enabled 变化都会立即协调 Enable/Disable。
- 测试：Scene lifecycle、GameSystem lifecycle、immediate enable/disable、Scripting API 不再暴露 `Behavior`，以及 Plugin runtime 直接继承 `GameBehavior` 的编译路径。
- 关闭标准：所有具有启停语义的 Component 直接复用唯一 `GameBehavior`，不保留 façade、别名或兼容层；Scene 级协调只使用唯一 `GameSystem`。已满足。

### ARCH-008：Play Runtime 普通日志退出后残留

- 历史证据：日志来源依赖 assembly scope，无法可靠定位一次 Play Session。
- 根因与影响：退出后 Debug/Info 污染 Console，跨多次 Play 混合。
- 当前实现：`LogSessionId` 是日志条目正式身份并参与 Collapse fingerprint；Console 默认 Clear on Play，进入下一次 Play 时清除普通历史日志但保留 current diagnostics，停止后保留本次 Play 的全部日志以便检查。策略开关由 `Editor/Diagnostics/Console/Clear on Play` Settings 独占，默认 `true`，Console Panel 不再持有重复配置。
- 测试：`PlayModeLogRetentionTests.CompletedPlaySessionRemovesOnlyTransientEntriesFromThatSession`、`FailedEntryRetainsPreparationDiagnosticsBecauseSimulationNeverStarted`。
- 关闭标准：只清理结束 Session 的普通日志，不影响其他 Session 或重要诊断。已满足。

### ARCH-009：Console Collapse 分组和布局不均

- 历史证据：Collapse 只合并连续项，卡片高度和右侧计数受文案长度影响。
- 根因与影响：同一日志被分成多组，视觉密度不一致；同文案不同 stack 又可能误合并。
- 当前实现：`EditorConsole` 使用包含 domain、severity、source、code、message、location 和 stack identity 的全局 fingerprint；组按最近 occurrence 排序，UI 使用固定折叠行和独立 Count 列。
- 测试：`CollapseGroupsEquivalentNonConsecutiveOccurrencesGlobally`、`CollapsedCardsUseOneUniformNativeItemSpacing`、`DistinctEntryDomainsDoNotReuseImGuiCardIdentity`。
- 关闭标准：非连续等价日志全局聚合，不同位置/stack 不合并。已满足。

### ARCH-010：Player Publisher 依赖引擎源码 checkout

- 历史证据：公开 Export request 暴露 Player project path，发布时向上查找 solution。
- 根因与影响：部署 Editor 无法独立构建 Player，公开 API 泄漏实现布局。
- 当前实现：`PlayerSupportPackCatalog` 以 `BuildTargetId` 解析不可变部署 closure；公开 request 不包含 `.csproj` 或 engine root。
- 测试：Support Pack 生成器在独立输出生成 closure；真实 E2E 仅向 Build 提供 Support Pack 路径。
- 关闭标准：删除旧 Publisher、playerProjectPath 和 solution 查找。已满足。

### ARCH-011：Player 携带编译工具和源内容

- 历史证据：旧 Player 包含 Roslyn、shaderc、texturec、C#/Shader source 和裸 Assets。
- 根因与影响：包体、启动抖动、权限风险和环境不确定性增加，内容部署边界失效。
- 当前实现：Support Pack 和 Player closure 明确禁止 Editor、Build、Compiler、Reload、Assets Pipeline、Plugins Authoring、toolchains、symbols 与源码；内容只发布 `catalog.inno`、hash pack 和 runtime manifest。Scripting 以 `CompileAuthoringGenerationAsync` 与 `CompileRuntimeDeploymentAsync` 区分工作流，Game Build 不创建 Editor API references、不编译 `.editor.cs`、不生成 Editor activation artifact。
- 测试：`SupportPackRejectsAuthoringAssetPipelineAssemblies`、`RuntimeDeploymentCompilationDoesNotCompileOrValidateEditorSources`、Build content enumeration、CI/E2E closure 检查。
- 关闭标准：Player 运行不调用编译工具且 Content 无裸创作源。已满足。

### ARCH-012：Export 缺少组合 generation snapshot

- 历史证据：Assets、Plugins、Settings、Scripting 各自取值，构建期间可能组合不同代际。
- 根因与影响：输出可重复性与引用闭包无法证明，切换时可能提交混合产品。
- 当前实现：Build 捕获 Assets/Plugins/Settings revision、Plugin snapshot、Settings bytes 和 pinned `SerializationGeneration`；脚本编译与 Target Artifact 在 staging 中并行，关键阶段重复验证 revision。
- 额外修复：`AssetPipeline.Save/Import` 现在作为正式 mutation commit 推进 revision 并发布 `AssetChangeSet`；后台 Artifact export 使用 owner-thread 捕获的 Serialization generation。
- 测试：`ChangedAuthoringGenerationCannotCommitAMixedBuildSnapshot`、`BackgroundArtifactExportUsesAnOwnerThreadSerializationSnapshot`。
- 关闭标准：任何 active generation 变化都阻止原子提交，旧 serialization snapshot 可安全完成在途读取。已满足。

### ARCH-013：Export 阻塞 Editor 并大量占用内存

- 历史证据：旧导出在第一次 await 前执行大量 IO，Plugin ZIP 将全部源与依赖驻留内存。
- 根因与影响：大型项目冻结 Editor，峰值内存随内容总量增长。
- 当前实现：Build 为异步阶段管线；owner thread 只捕获快照，Artifact、Content Pack、ZIP、Support Pack 均采用流式文件 IO；请求支持 progress 和 cancellation。
- 测试：Game/Plugin cancellation 均验证无产品与 staging；大 payload 通过文件流复制而非整包 byte array。
- 关闭标准：可取消阶段不阻塞 Editor frame，失败没有半成品。已满足。

### ARCH-014：缺少 Build Profile

- 历史证据：Export Modal 临时持有产品字段，无法成为稳定构建输入。
- 根因与影响：CLI、Editor 和 CI 无法共享同一可验证配置。
- 当前实现：`BuildProfile`、`BuildProfileStore`、`BuildTargetId` 是统一 current-format 协议；Editor 和 CLI 调用同一 Build Pipeline。
- 测试：Profile round-trip、非法跨平台产品名、损坏格式、原子保存测试；当前 InnoProject 已生成 `BuildProfile.inno`。
- 关闭标准：Profile 与 UI 分离且不含旧字段、schema version 或 fallback。已满足。

### ARCH-015：Plugin 曾依赖 .iplugin

- 历史证据：File Browser 曾要求用户创建 Plugin Definition Asset。
- 根因与影响：项目与 package authoring 出现第二套事实来源，用户重复维护清单。
- 当前实现：File 菜单 `Export as Plugin` 直接从 Project 和 active generation 自动生成 `Plugin.inno`；`.iplugin`、Definition 类型、Importer 和菜单均删除。
- 测试：`PluginBuildIsDeterministicSourceOnlyAndUsesTheInstallContract`；全仓禁用项搜索与架构检查。
- 关闭标准：正常工作流不创建 companion asset。已满足。

### ARCH-016：Plugin 内嵌依赖闭包不完整

- 历史证据：旧实现把 active Plugin 当作扁平列表，缺少完整传递闭包、循环和冲突约束。
- 根因与影响：安装后依赖缺失、重复 ID 或顺序不确定。
- 当前实现：稳定 Plugin ID 图进行确定性拓扑排序；可选内嵌输出扁平完整 ZIP 闭包，安装侧验证声明、循环、重复 ID、路径和 archive limits。
- 测试：`DependencyGraphUsesDeterministicTopologicalOrderAndRejectsCycles`、`MissingDependenciesDuplicateIdsAndInvalidOverridesRejectTheCandidateGeneration`、确定性 Plugin Build 测试。
- 关闭标准：相同输入得到字节相同 ZIP，缺失/循环/冲突在候选阶段失败。已满足。

### ARCH-017：Plugin 主线程高频全目录扫描

- 历史证据：`PluginEnvironment.Update` 每 500ms 在 owner thread 递归计算完整目录指纹。
- 根因与影响：Folder Plugin 增大后形成持续主线程 IO 峰值。
- 当前实现：递归 `FileSystemWatcher` 只发布变化信号，150ms 防抖后在 owner thread 执行候选激活；30 秒漏事件对账在 worker 计算 fingerprint，不在逐帧主线程扫描。
- 测试：`PluginSourceServiceTests.SourceWatcherDebouncesChangesAndActivatesOnTheOwnerThread`。
- 关闭标准：无变化时 `Update` 不遍历目录；文件变化合并为一次 owner-thread refresh。已满足。

### ARCH-018：11 处 InternalsVisibleTo

- 历史证据：Rendering、Assets、Scene 和测试边界依靠 friend assembly。
- 根因与影响：程序集并非真实领域边界，测试可穿透封装。
- 当前实现：重新移动 owner、合并 Rendering Core、提取真实 public production contract；全部 friend 声明删除。
- 测试与守卫：Architecture Tool 扫描 production/tests，禁止 `InternalsVisibleTo` 和 non-public reflection。
- 关闭标准：全仓零 friend，测试只经公开契约。已满足。

### ARCH-019：静态 Manager 将进程绑定为单 Session

- 历史证据：`Shell` 和多个静态 Manager 共同拥有进程状态。
- 根因与影响：多项目、Preview、Play/Edit 并存和并行测试无法隔离。
- 当前实现：`EngineHost` 拥有 ModuleHost、TypeCatalog、SerializationRegistry、Diagnostics、Logging 等实例服务；`RuntimeSession` 拥有 Scene/Time/Input/Lifecycle 状态。脚本静态门面只解析 AsyncLocal execution context。
- 测试：`MultipleHostsAndSessionsKeepSceneAndTimeStateIsolated`、`ScriptFacadesRejectCallsOutsideAnActiveSession`、`DisposedHostRejectsNewSessions`。
- 关闭标准：真实状态无静态 Manager owner，多 Host 可并行。已满足。

### ARCH-020：Serialization 手写 Converter 成本过高

- 历史证据：普通 DTO、Manifest 和 Settings 都需要重复 converter。
- 根因与影响：可读性差、遗漏晚失败、内部 DTO 被迫 public。
- 当前实现：生成器处理普通封闭对象；显式 converter 仅保留多态、身份、图、自定义不变量和外部类型。
- 测试：Serialization Generator 诊断、round-trip、required converter 和 pinned generation worker continuation。
- 关闭标准：没有 JSON 旁路或新旧 converter 双路径。已满足。

### ARCH-021：Shell、AssetManager、ScriptManager 巨型职责中心

- 历史证据：composition、编译、reload、缓存、UI workflow 被少量 manager 集中持有。
- 根因与影响：owner 模糊、依赖方向倒置、修改 blast radius 大。
- 当前实现：Shell 删除并由 Runtime hosting 取代；Asset runtime database 与 authoring pipeline 分离；Scripting 分成 Api、Compiler、Reload；Build 阶段内部按功能组织，Editor 只保留 workflow/presentation。
- 测试与守卫：ProjectReference 方向、Player closure、removed project 检查；各 bounded context 有独立测试项目。
- 关闭标准：原三个中心及旧项目不存在，Composition Root 只在 Editor Application、Player、Build CLI。已满足。大型内部实现仍可按独立职责继续演进，但不再承担跨领域 owner。

### ARCH-022：Observer 异常被吞掉

- 历史证据：部分 callback 捕获异常后无日志、无诊断且继续重复调用。
- 根因与影响：主事务虽保持稳定，但扩展失败不可见并可能每帧重复。
- 当前实现：候选 observer 聚合失败并 rollback；Diagnostic sink 失败会 quarantine 并发布 `sinkFailed`；Log sink 失败会原子移出路由并通过 `sinkFailed` 事件报告，其余 sink 继续工作。
- 测试：`SinkFailure_DoesNotPreventOtherSinksFromReceivingState`、`FailingSinkIsReportedQuarantinedAndDoesNotBlockHealthySinks`、Plugin candidate rollback 测试。
- 关闭标准：没有空 catch 吞掉扩展失败；已提交事务不被 presentation failure 回滚。已满足。

### ARCH-023：Scripting 没有 Compilation Ticket

- 历史证据：Play/Export 轮询全局 state，无法证明结果属于请求的 source generation。
- 根因与影响：慢旧编译可能覆盖新请求，调用者无法等待或取消自己的请求。
- 当前实现：Compiler/Editor workflow 使用 request identity 与 `IScriptCompilationTicket`；Reload 只接受仍匹配 source/reference/plugin generation 的 last request。
- 测试：`NewCompilationTicketSupersedesTheExactPreviousRequest`、`PreCanceledCompilationDoesNotPublishAnArtifactGeneration`、`SuccessfulReloadActivatesCandidateAndFailedCompilationRetainsIt`。
- 关闭标准：stale result 永不激活。已满足。

### ARCH-024：Play 编译和准备状态不透明

- 历史证据：用户只能看到等待，无法区分 compile、prepare、stop 或 failure。
- 根因与影响：交互不可解释，取消和失败恢复边界不清楚。
- 当前实现：`EditorPlayModeState` 明确 Editing、Compiling、Preparing、Playing、Stopping、Failed；Toolbar 通过只读接口呈现状态，Controller 协调异步 ticket 和 session。
- 测试：进入等待、编译失败、准备前取消、模拟失败和 host loop 生命周期测试。
- 关闭标准：每条转移可观察、可取消且失败回到可编辑状态。已满足。

### ARCH-025：架构规则没有自动执行

- 历史证据：friend、错误引用、XML suppression 和 Native 泄漏只能人工发现。
- 根因与影响：文档规范会随时间失效。
- 当前实现：`Inno.Tooling.Architecture` 检查禁用实现、引用方向、循环、Player closure、removed projects、static managers、脚本门面、Native 泄漏、测试反射和多行 XML；CI 在 build/test 前运行。
- 测试：工具自身作为 solution project 构建；本次验证命令 `dotnet run --project tools/Inno.Tooling.Architecture -- .`。
- 关闭标准：违反规则产生非零退出码。已满足。

### ARCH-026：Export、Scripting、Logging 前后端杂糅

- 历史证据：Panel/Module 同时进行 IO、进程发布、编译和日志存储。
- 根因与影响：UI 技术渗入领域层，headless/CLI 无法复用。
- 当前实现：Build、Scripting Compiler/Reload、Editor Diagnostics 是独立后端；`Inno.Editor.Exporting`、`Inno.Editor.Scripting` 和 Logging Panel 只负责 workflow/presentation；Editor Application 注入组合。
- 测试与守卫：Build 禁止引用 Editor，Runtime/Player closure 禁止 Editor/Build，Panel 使用只读 `IEditorConsole`。
- 关闭标准：后端可由 CLI/Player/测试独立组合。已满足。

### ARCH-027：Inno.Core.Framework 所有权错误

- 历史证据：名为 Core 的项目反向组合 Assets、Plugins 和高层宿主。
- 根因与影响：Core 不再是依赖图底层，任何模块都可能借 Core 引入业务依赖。
- 当前实现：项目和 namespace 删除；hosting 移到 `Inno.Runtime`，Composition Roots 位于 Application/Player/CLI。
- 守卫：removed project 清单与 Core 引用方向检查。
- 关闭标准：solution、源码和文档无 `Inno.Core.Framework` 稳定 API。已满足。

### ARCH-028：Inno.Platform 直接泄漏 SDL 实现

- 历史证据：上层 public/protected API 可看到 SDL enum、pointer 或 window 类型。
- 根因与影响：平台 contract 与单一 backend 绑定，测试和替换困难。
- 当前实现：`Inno.Platform` 只保留 `IPlatformApplication`、`IPlatformWindow`、neutral options/handles；SDL3 实现在 `Inno.Platform.Sdl3`，ImGui bridge 独立。
- 测试与守卫：Architecture Tool 限制 SDL native consumer 并扫描 native signature leakage。
- 关闭标准：上层 contract 无 SDL 类型。已满足。

### ARCH-029：Rendering 拆分依靠 friend assembly

- 历史证据：Rendering Core、Runtime、BGFX 通过 friend 访问实现成员。
- 根因与影响：项目拆分按文件角色而非稳定部署/替换边界，封装是假的。
- 当前实现：backend-neutral core 合并为 `Inno.Rendering`；Runtime、Assets、Scene、ShaderGraph、Bgfx、Bgfx.ImGui 各自承担真实集成边界；只有 BGFX adapter 引用 Native BGFX。
- 测试：RenderGraph、pipeline resource、runtime layer、shader graph candidate 和 BGFX device tests；Architecture native consumer rules。
- 关闭标准：零 friend，Core 不引用 Scene/ShaderGraph/Editor。已满足。

### ARCH-030：测试非确定性和 Player E2E 缺失

- 历史证据：旧全量测试出现过 Roslyn 偶发失败，Game Export 只使用 Fake Publisher。
- 根因与影响：无法证明脱离源码的真实 Player 能启动、创建图形 backend、运行帧并有序退出。
- 当前实现：`tests/Inno.Player.E2E` 创建临时 Project、fresh script generation、Artifact closure、Build、启动导出 Player 并验证帧与退出；CI matrix 在 macOS ARM64 和 Windows x64 构建 Native/Support Pack 后执行同一 E2E。
- 当前证据：macOS ARM64 本机 E2E 已成功，Metal/BGFX 初始化并运行 3 帧；Windows x64 代码、Support Pack 生成和 CI job 已配置，但当前 macOS 主机不能执行 Windows 进程。
- 关闭标准：两个目标 runner 都产生一次成功 E2E 记录。状态保持“待平台验证”，在 Windows CI 实际成功前不得写成已关闭。

### ARCH-031：保留现有 API 阻碍正确领域建模

- 历史证据：旧 Manager、namespace façade、publisher request 和项目名因调用方数量被保留。
- 根因与影响：错误 owner 被兼容层永久化。
- 当前实现：全部调用方硬切到最终 API；没有旧 overload、forwarder、facade 或 obsolete wrapper。
- 守卫：removed project、TypeForwardedTo、Obsolete、禁用实现名扫描。
- 关闭标准：当前 API 是唯一运行路径。已满足。

### ARCH-032：旧 API/旧格式兼容机制可能重新进入代码

- 历史证据：重构容易添加 fallback reader、schemaVersion 或 migration 目录作为过渡。
- 根因与影响：正常路径同时维护多代协议，测试矩阵和复杂度持续增长。
- 当前实现：Project/Editor Settings、Build Profile、Plugin Manifest、Artifact/Catalog 只读写当前格式；当前 InnoProject 数据已直接重写，缓存直接重建。
- 守卫：Architecture Tool 禁止 Legacy/Compatibility/Migration/Former/Deprecated 实现名、Obsolete、forwarder 与 schema compatibility 字段。
- 关闭标准：无 fallback、双写、旧字段别名和迁移测试。已满足。

### ARCH-033：为减少程序集或方便测试而过度公开 API

- 历史证据：friend 清除可能被机械替换为 public，测试也可能推动 production 后门。
- 根因与影响：实现细节变成永久扩展承诺，跨项目耦合反而增加。
- 当前实现：Build stage、Artifact writer、Player composer、registry snapshot 实现保持 internal；只公开正式脚本/Plugin 扩展点、真实跨程序集协议、Composition Root 和不可变部署模型。
- 守卫与审查：测试禁止 private reflection/friend；Wiki 按当前项目逐页列出正式 API，ProjectReference 方向由工具验证。
- 关闭标准：不存在 test-only production API 或 friend replacement façade。已满足。

### ARCH-034：Attribute 发现可能保留 collectible 类型或旧委托

- 历史证据：长期 registry 若直接保存跨 generation `Type`、实例或 delegate，会阻止 collectible ALC 卸载。
- 根因与影响：热重载内存泄漏、旧实现继续响应事件、候选 rollback 不完整。
- 当前实现：Type/Module candidate 构建不可变 snapshot，先验证再原子 Activate；旧 snapshot 显式 Dispose，持久状态只保存 Stable ID 与中立 payload。
- 测试：`LoadAndReloadPublishOnlyTheActiveGeneration`、`RollbackRestoresThePreviousTypeAndRegistrySnapshot`、`CompletedReloadAllowsThePreviousContextToUnload`、`CandidateActivationFailureRestoresEveryRegistrySnapshot`、Shader Node generation disposal 测试。
- 关闭标准：失败候选不影响 active generation，退休 ALC 在释放外部 lease 后可收集。已满足。

### ARCH-035：活动 Folder Plugin 直接依赖可变安装目录

- 历史证据：运行中删除或原地更新 `Plugins/<folder>` 后，旧 AssetLoader 在 activation、rollback、FileBrowser draw、shutdown 和下一帧 `Update` 中继续枚举已不存在的 `Plugins/<folder>/Assets`，最终形成 `DirectoryNotFoundException`、reload rollback failure 和进程级未处理异常。
- 根因与影响：ZIP Plugin 使用 `Library/Plugins/<pluginId>/<contentHash>` 解压 snapshot，但 Folder Plugin 的 active Source Mount 直接指向安装目录；所谓 active generation 并不不可变，外部文件操作能够同时破坏候选和 last-good。
- 当前实现：ZIP 与 Folder 在 Scan 阶段统一物化为内容寻址、原子提交的 Library generation snapshot；Folder 的 manifest、内容校验和 hash 基于已复制 snapshot，active/candidate mount 都只读取 snapshot。外部源删除或更新只改变下一候选，不会破坏事务 previous snapshot。安装集合中的删除、结构失效、Asset metadata 候选失败或代码更新编译失败会提交 unavailable generation，而不是用 rollback 继续伪装旧 Plugin 有效；Scripting 根据 Plugin ID/content hash 退休变化 module 及完整反向依赖闭包，Scene 使用 Missing Component/System 保存 Stable ID、persistent ID、状态和顺序。只有 reload participant/状态迁移本身失败才 rollback。
- 测试：`InstalledFolderPluginUsesTheSameReadOnlyMountContractAsZip` 验证更新前后 snapshot 路径和内容隔离；`RemovingAnActiveCodeFolderKeepsTheLastGoodSnapshotUntilAtomicRemovalCommits` 验证 source 删除期间 previous snapshot 始终可读；`StructurallyInvalidActivePluginStagesAnUnavailableGeneration` 与 `InvalidAssetMetadataUpdateStagesAnUnavailableGenerationWithoutThrowing` 验证安装结构和 Asset 候选失败被隔离为 unavailable generation；`RemovedOrBrokenUpdatedPluginCommitsUnavailableGenerationAndRecovers` 使用真实 Plugin/Project Roslyn 编译、ModuleHost closure 和 Scene reload 验证物理删除及原地坏更新即使令 Project Scripts 编译失败也会退休 Plugin + Runtime/Editor Scripts、显示 Missing，并在同 Stable ID 回归后恢复类型、persistent ID 和序列化状态；`RemovingAndRestoringAPluginGenerationPreservesItsComponentAsMissingState` 继续覆盖底层 collectible Plugin Scene 迁移协议。
- 关闭标准：外部删除、结构损坏、Asset metadata 损坏或原地代码更新不会破坏 active/rollback snapshot，也不会从 Refresh、File Browser、reload、shutdown 抛出失效路径异常；当前不可成立的代码 closure 不会继续运行，Scene 显示可恢复 Missing，恢复有效内容后自动重建。已满足。

### ARCH-036：Rendering participant 拒绝 Plugin 缺席代际并回滚卸载

- 复现证据：Editor 关闭后删除 `inno.rendering.2d` 再启动时会正确显示无 Rendering Provider 与 Missing Component；但运行中删除同一 Plugin 时出现 `RENDER_EXTENSION_RELOAD_REJECTED`，Scene 继续渲染，Inspector 仍显示 `SpriteRenderer2D`，说明旧 Plugin generation 没有退休。
- 根因与影响：`RenderRuntimeReloadSession` 对每个已跟踪 `RenderPipelineAsset` 无条件调用严格的 generation constructor。候选 TypeCache 已移除 Plugin Pipeline/Feature Stable ID 时，缺席被错误地转成异常，统一 Editor reload coordinator 因而回滚 Assembly、TypeCache、Scene、Rendering 和 Plugin generation。运行时行为与冷启动不一致，collectible ALC 仍被旧 Pipeline/Feature/Request Provider 固定。
- 当前实现：Rendering registry 在实例化前分别验证资产结构与扩展可用性。空 Stable ID、重复 Feature、构造或配置失败仍是拒绝候选的真实错误；候选目录中不存在被引用 Pipeline/Feature Stable ID 则产生合法 unavailable generation。事务会原子发布空 generation、空 Request Provider generation，并在提交后释放旧实例；被跟踪但当前 unavailable 的资产仍参与后续候选准备，同 Stable ID 回归时在事务内恢复。Scene 与 Editor Provider registry 因同一 TypeCache commit 同步进入 Missing/无 Provider 状态。
- 测试：`RemovingAndRestoringPluginRenderingCommitsTheSameUnavailableStateAsColdStart` 使用真实 collectible Plugin module，同时覆盖 Plugin-owned Pipeline 缺席和 host Pipeline 引用 Plugin-owned Feature 缺席；验证卸载不产生 `RENDER_EXTENSION_RELOAD_REJECTED`、不可用期间不再执行旧 Graph、恢复后重新执行。`RemovedOrBrokenUpdatedPluginCommitsUnavailableGenerationAndRecovers` 与 `RemovingAndRestoringAPluginGenerationPreservesItsComponentAsMissingState` 覆盖同一事务中的代码 closure 退休、Scene Missing 和 Stable ID 恢复。
- 关闭标准：运行中移除 Rendering Plugin 与缺失 Plugin 后冷启动得到相同可观察状态；旧 Pipeline、Feature、Request Provider、Viewport Provider 和 Scene runtime 类型全部退休；相同 Stable ID 回归后自动恢复。已满足。

### ARCH-037：Play Session 与 Inspector 锁定对象固定退休 ALC

- 复现证据：Play 中执行 `Reload Plugins` 后 transaction 已成功提交，但 `INNO-ALC-UNLOAD` 同时报告 `RuntimeScripts`、`EditorScripts` 与 `Plugin.inno.rendering.2d` 在十次完整 GC 后仍可达。
- 根因与影响：独立 Play `RuntimeSession` 没有注册为 assembly reload participant，运行 Scene 仍强引用旧 Plugin Component 与旧 Project Script；Plugin ALC 被固定后，引用它的 Runtime/Editor Scripts ALC 形成依赖链并一起无法卸载。Inspector lock 还可能直接强引用 collectible target。进一步的集成测试暴露 TypeRegistry candidate 已在 staging 阶段捕获 Play `SceneTypeRegistry`，若随后销毁 Play Session，旧实现仍会尝试激活已 Dispose 的 registry。
- 当前实现：`EditorPlayModeController` 实现正式 reload participant，并由 Play Mode Module 在生命周期内注册；candidate activation 前同步释放 Play Scene lease、Runtime Session 与隔离 History，原子恢复 Edit presentation。Play simulation 是瞬态状态，candidate 回滚也不恢复旧 simulation。`TypeRegistry.RegistryTransaction` 将 preparation 后撤销的 registry 视为合法 retired participant，释放 previous/candidate snapshot 并跳过 activation。Inspector lock 对 `IdentityObject` 只保存 persistent ID 并从当前 Session 重解析，对其他 collectible target 只保留弱引用。
- 测试：`PluginReloadQuiescesPlaySessionBeforeRetiringItsAssemblyGeneration` 使用真实 collectible Plugin Component 进入 Play，执行完整 Plugin/Runtime/Editor reload，验证回到 Editing、Scene/History lease 各释放一次且所有 retirement monitor 成功；`RegistryRetiredAfterCandidatePreparationIsExcludedFromActivation` 验证 candidate preparation 与 activation 之间 Dispose registry 不会激活已销毁 owner，并释放前后两个 snapshot；`Inno.Editor.PlayMode.Tests` 全量验证既有状态机和 Scene 隔离行为不回归。
- 关闭标准：Play 中触发 Plugin/Scripting reload 必须先恢复 Edit，不发生 `ObjectDisposedException`，旧 Play 世界、Inspector 锁定目标及 prepared registry 均不固定 collectible generation，`INNO-ALC-UNLOAD` 不再出现。已满足。

## 已确认优势

本次整改保留并强化了以下正确基础，而不是推倒重来：

- collectible AssemblyLoadContext、候选加载、回滚和卸载监控；
- Stable Type ID、Type Catalog generation 和原子 Registry snapshot；
- 统一 Asset Catalog、persistent ID、CAS Artifact 和依赖图；
- Project/Plugin 共用 Asset Pipeline 和只读 Source Mount；
- Editor Extension 的 Attribute、稳定 ID、构造注入和 quarantine；
- reload-safe History protocol 与失败原子性；
- 后端中立 Rendering、RenderGraph 和统一 Shader IR；
- Play Mode 的编译门禁、History 隔离和显式状态机；
- 每项目显式 Scripting API Export 和 logical namespace。

## 最终验证入口

```text
dotnet run --project tools/Inno.Tooling.Architecture -- .
dotnet build InnoEngine.sln --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test InnoEngine.sln --no-build --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet run --project build/support/Inno.Build.SupportPacks -- --target macos-arm64 ...
dotnet run --project tests/Inno.Player.E2E -- --target macos-arm64 ...
```

Windows x64 的最后一条 E2E 必须在 Windows runner 执行；CI 定义位于 `.github/workflows/rendering-ci.yml`。

## 2026-09-01 本机验收记录

- `InnoEngine.sln` Debug build：0 warnings、0 errors。
- `Inno.Tooling.Architecture`：通过。
- 全量测试：558 passed、0 failed、0 skipped。
- macOS ARM64 Support Pack：从 Release self-contained Player 成功生成，closure 校验通过。
- macOS ARM64 Player E2E：Game Build 原子提交；仅编译 Runtime scripts；导出 `.app` 在 Metal/BGFX 下完成 3 帧并正常 shutdown。
- Windows x64：实现、测试输入、Support Pack target 和 CI runner 已就绪；仍等待 Windows runner 的真实进程验证，因此 ARCH-030 保持“待平台验证”。

## 2026-09-02 Play reload 生命周期验收记录

- Editor Application Debug build：0 warnings、0 errors。
- `Inno.Tooling.Architecture`：通过。
- 全仓测试复跑：590 passed、0 failed、0 skipped。
- `PluginReloadQuiescesPlaySessionBeforeRetiringItsAssemblyGeneration`：真实 collectible Plugin Component、Play Session、Plugin/Runtime/Editor generation reload 与 ALC unload verification 全部通过。
- `RegistryRetiredAfterCandidatePreparationIsExcludedFromActivation`：candidate preparation 后退休的 session registry 不再被激活，previous/candidate snapshots 均释放。

## 台账维护规则

- 关闭问题必须同时更新实际测试名和最后验证结果。
- 仅添加日志、catch 或 fallback 不构成整改。
- 不允许通过兼容读取、旧 API 包装、friend assembly、测试反射或 public 后门关闭问题。
- 新问题使用新的稳定 ID，不复用已关闭编号。
