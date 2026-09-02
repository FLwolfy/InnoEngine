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
| ARCH-035 | P0 | 已关闭 | Plugins.Authoring | 3 |
| ARCH-036 | P0 | 已关闭 | Rendering.Runtime / Editor.Scripting | 5、7 |
| ARCH-037 | P0 | 已关闭 | Editor.PlayMode / Editor.Inspection | 4、7 |
| ARCH-038 | P1 | 已关闭 | Rendering.Runtime | 5 |
| ARCH-039 | P1 | 已关闭 | Inno.Rendering.2D Plugin | 5 |
| ARCH-040 | P1 | 已关闭 | Scene | 4 |
| ARCH-041 | P1 | 已关闭 | Rendering.Bgfx / Platform.Sdl3.ImGui | 5 |
| ARCH-042 | P0 | 已关闭 | Rendering.Bgfx / Native toolchains | 5 |
| ARCH-043 | P1 | 已关闭 | Editor.Inspection | 7 |
| ARCH-044 | P0 | 已关闭 | Rendering.Runtime | 5 |
| ARCH-045 | P2 | 已关闭 | Editor.ImGui | 7 |
| ARCH-046 | P2 | 已关闭 | Editor.Panel.Inspector | 7 |
| ARCH-047 | P1 | 已关闭 | Inno.Rendering.2D Plugin | 5 |
| ARCH-048 | P0 | 已关闭 | Extensibility.Modules / Scripting.Reload | 2、3 |
| ARCH-049 | P2 | 已关闭 | Editor.ImGui / Editor.Panel.Inspector | 7 |
| ARCH-050 | P1 | 已关闭 | Inno.Rendering.2D Plugin | 5 |
| ARCH-051 | P2 | 已关闭 | Editor.ImGui / SceneView / GameView | 7 |
| ARCH-052 | P1 | 已关闭 | Editor.Panel.GameView / Editor.Settings | 7 |
| ARCH-053 | P1 | 已关闭 | Inno.Rendering.2D Plugin | 5 |
| ARCH-054 | P0 | 已关闭 | Build.Cli / Build.SupportPacks / Assets.Pipeline | 6 |
| ARCH-055 | P0 | 已关闭 | Player / Runtime Module Generation | 6 |
| ARCH-056 | P0 | 已关闭 | Editor.Rendering / Rendering.Runtime / Rendering Plugins | 5、7 |

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
- 关闭标准：运行中移除 Rendering Plugin 与缺失 Plugin 后冷启动得到相同可观察状态；旧 Pipeline、Feature、Request Provider、Viewport Contributor 和 Scene runtime 类型全部退休；相同 Stable ID 回归后自动恢复。已满足。

### ARCH-037：Play Session 与 Inspector 锁定对象固定退休 ALC

- 复现证据：Play 中执行 `Reload Plugins` 后 transaction 已成功提交，但 `INNO-ALC-UNLOAD` 同时报告 `RuntimeScripts`、`EditorScripts` 与 `Plugin.inno.rendering.2d` 在十次完整 GC 后仍可达。
- 根因与影响：独立 Play `RuntimeSession` 没有注册为 assembly reload participant，运行 Scene 仍强引用旧 Plugin Component 与旧 Project Script；Plugin ALC 被固定后，引用它的 Runtime/Editor Scripts ALC 形成依赖链并一起无法卸载。Inspector lock 还可能直接强引用 collectible target。进一步的集成测试暴露 TypeRegistry candidate 已在 staging 阶段捕获 Play `SceneTypeRegistry`，若随后销毁 Play Session，旧实现仍会尝试激活已 Dispose 的 registry。
- 当前实现：`EditorPlayModeController` 实现正式 reload participant，并由 Play Mode Module 在生命周期内注册；candidate activation 前同步释放 Play Scene lease、Runtime Session 与隔离 History，原子恢复 Edit presentation。Play simulation 是瞬态状态，candidate 回滚也不恢复旧 simulation。`TypeRegistry.RegistryTransaction` 将 preparation 后撤销的 registry 视为合法 retired participant，释放 previous/candidate snapshot 并跳过 activation。Inspector lock 对 `IdentityObject` 只保存 persistent ID 并从当前 Session 重解析，对其他 collectible target 只保留弱引用。
- 测试：`PluginReloadQuiescesPlaySessionBeforeRetiringItsAssemblyGeneration` 使用真实 collectible Plugin Component 进入 Play，执行完整 Plugin/Runtime/Editor reload，验证回到 Editing、Scene/History lease 各释放一次且所有 retirement monitor 成功；`RegistryRetiredAfterCandidatePreparationIsExcludedFromActivation` 验证 candidate preparation 与 activation 之间 Dispose registry 不会激活已销毁 owner，并释放前后两个 snapshot；`Inno.Editor.PlayMode.Tests` 全量验证既有状态机和 Scene 隔离行为不回归。
- 关闭标准：Play 中触发 Plugin/Scripting reload 必须先恢复 Edit，不发生 `ObjectDisposedException`，旧 Play 世界、Inspector 锁定目标及 prepared registry 均不固定 collectible generation，`INNO-ALC-UNLOAD` 不再出现。已满足。

### ARCH-038：GraphicsSettings 保存进程级静态可变状态

- 历史证据：default pipeline、capabilities 和 frame statistics 由静态字段持有；第二个 Runtime 会覆盖第一个 Runtime 的脚本观察值。
- 根因与影响：Unity 风格调用形式被错误地等同于 process-global owner，破坏多 `RuntimeSession` 隔离并让测试顺序影响状态。
- 当前实现：`GraphicsSettings` 只作为脚本 façade，通过 `AsyncLocal` 解析当前 `RenderRuntimeLayer` 私有的 `GraphicsSettingsState`。Editor/Player composition root 只在当前帧执行边界进入 `EnterExecutionScope()`，引擎内部直接使用实例状态；无 scope 时读取为 null、写入明确失败。
- 测试：`GraphicsFacadeResolvesTheCurrentlyBoundRenderingRuntime` 验证两个 Runtime 的嵌套 scope、统计与 default pipeline 完全隔离，并验证退出 scope 后不残留状态。
- 关闭标准：静态 façade 不拥有状态，多个 Runtime 不互相覆盖。已满足。

### ARCH-039：Rendering2D 按 Camera 重复全 Scene 扫描并分配

- 历史证据：每个 Camera、每帧分别遍历全部 GameObject，重复查找 Sprite、Tilemap、Light 并创建 List/Array。
- 根因与影响：Scene extraction 没有 Scene owner；成本近似 `camera count × object count`，大场景产生稳定 GC 压力。
- 当前实现：官方 2D Plugin 提供唯一 `Rendering2DSceneSystem : GameSystem`。它用 Scene 不可变结构快照 identity 做失效键，只在对象或 Component 结构变化时重建 Camera/Drawable/Light 索引；所有 Camera 共享同一快照并读取 Component 当前值。Disable/Destroy 会清空全部 Plugin 引用。
- 测试与验收：Scene 的 `GameBehaviorLifecycle_ReindexesAfterStructuralChanges` 验证结构 revision；2D Plugin 由真实 Editor 120-frame Metal smoke 验证一个 SceneSystem 为全部 Camera 提供 frame snapshot，且退出无 retained Plugin/GPU owner。
- 关闭标准：稳定场景不再按 Camera 扫 Scene 或创建 extraction List/Array，结构变化后下一次 capture 准确重建。已满足。

### ARCH-040：无帧回调 Renderer 仍进入全部生命周期遍历

- 历史证据：Renderer 与脚本统一为 `GameBehavior` 后，即使没有 override Update/Fixed/Late，也会进入同一逐帧 traversal。
- 根因与影响：类型能力在运行时每帧才通过虚调用自然落空；组件规模增大时产生无意义 dispatch。
- 当前实现：Scene type candidate 构建阶段一次反射生成 lifecycle phase mask；Runner 为 activation、一次性 Start、Update、FixedUpdate、LateUpdate 维护独立索引。activation 只在结构或 enabled/hierarchy 变化时同步，Start 成功后立即退队；没有覆盖对应帧 callback 的 GameBehavior 不进入该数组。replacement/removal 会立即释放旧索引引用，未引入第二基类或隐藏接口。
- 测试：`GameBehaviorLifecycle_DispatchesLifecycleCallbacks` 与 `GameBehaviorLifecycle_ReindexesAfterStructuralChanges` 覆盖阶段分派和增删后的索引重建。
- 关闭标准：唯一 `GameBehavior` API 保持不变，逐帧 dispatch 只包含真实参与者。已满足。

### ARCH-041：BGFX 与 ImGui backend 的进程单实例状态边界不明确

- 历史证据：第二个 BGFX Device 或 ImGui context 会共享/覆盖静态 native routing 状态。
- 根因与影响：实现实际上只支持一个图形设备，却允许调用方构造出不受控的第二 owner；ImGui callback 无法确定 viewport 属于哪个 context。
- 当前实现：BGFX 用 process device lease 在 native init 前拒绝第二个活动设备，并正确处理 init 失败与 shutdown 后释放；single-threaded 模式的进程一次性限制也显式建模。ImGui 只保留不可变 native callbacks，按当前 `ImGuiContext` 路由到 context-owned viewport/window/renderer map，Dispose 精确注销 owner。
- 测试：`SecondDeviceIsRejectedWhileTheProcessRuntimeIsOwned` 与完整 Editor native smoke 覆盖并发拒绝、真实 context/backend 组合和 teardown。
- 关闭标准：平台约束被 API 主动执行，不存在 last-writer-wins 全局 backend 状态。已满足。

### ARCH-042：GPU shutdown 所有权闭包与 native 产物不确定

- 历史证据：Editor 正常退出时 DefaultSprite/ImGui Shader 与 Metal resource 输出 `RefCount` mismatch；重新编译 BGFX 后 Editor 仍可能加载输出目录中的旧 dylib。
- 根因与影响：Shader 同时由托管层和 Program 分别销毁；native loader 把“已部署”误当作“当前”；BGFX Metal Debug 使用 Objective-C `retainCount` 猜测应用所有权，而 Metal framework/driver 也会内部 retain。
- 当前实现：Program 创建时原子接管阶段 Shader，托管层只销毁 Program；Device shutdown 前采集所有 managed resource table 与 deferred queue 的闭包状态，即使失败也保证 native shutdown、readback buffer 和 process lease 完整释放后再报告。开发启动按 SHA-256 内容身份同步 `.lib` 当前 native 产物。Metal vendor 层保留真实 release，删除无法证明所有权的 `retainCount` 断言，以确定性的 Inno/BGFX handle closure 代替。
- 测试与验收：BGFX 21 项设备测试全部通过；macOS ARM64 Debug Editor 120-frame Metal smoke 退出包含 `BGFX Shutdown complete`，且无 `RefCount is`、BGFX Fatal、resource closure 或未处理异常。
- 关闭标准：真实资源遗漏会被明确报告且不会把 native runtime 留在半关闭状态；正常完整关闭不再输出伪泄漏警告，也不会运行陈旧 native 产物。已满足。

### ARCH-043：Inspector Drawer 使用静态 path 文本缓冲

- 历史证据：数字、Guid 和集合 Drawer 以 property path 索引静态字典。
- 根因与影响：不同对象、窗口、Editor Host 的同名路径串状态；静态 key/value 生命周期无法随 inspected owner 释放。
- 当前实现：状态由 `SerializedPropertyRenderer` 实例拥有，以 owner 的 `ConditionalWeakTable`、完整 path 和 drawer-local key 隔离。`PropertyDrawContext` 提供中立字符串状态 API；Drawer 不再维护静态 buffer，owner 回收即释放整组状态。
- 测试：`TextEditStateIsScopedByRendererOwnerAndPropertyPath` 验证相同 path 的不同 owner 以及同一 owner 的不同 renderer 不共享状态。
- 关闭标准：不存在 Drawer 静态编辑 buffer，多窗口/多 Host 不串值且不固定 collectible target。已满足。

### ARCH-044：已完成 Rendering reload transaction 保留旧 generation

- 复现证据：Plugin 移除已经成功提交 Missing/unavailable 状态，但 retirement monitor 仍报告 Plugin、RuntimeScripts 和 EditorScripts ALC 在十次 GC 后可达；纯脚本 reload 通常不复现。
- 根因与影响：`RenderRuntimeReloadSession` 为 rollback 捕获 previous Pipeline、Request Provider、pending/current request；`Finish()` 只 Dispose old instance，却没有清空 transaction 字段。统一 reload coordinator 或诊断链暂时保留已完成 transaction 时，其中的 Plugin 对象、frame payload 或 delegate 会固定 Plugin ALC；Runtime/Editor Scripts 引用 Plugin API，因此依赖 closure 一起存活。Rendering2D 还会把 Plugin 类型放入 SceneSystem extraction 和 viewport resource，扩大可达图，这正是纯脚本 reload 没有相同现象的原因。
- 当前实现：activation 前先释放 viewport/render resources 和当前请求；Scene 进入 host-owned Missing；Rendering transaction 在 Finish 后清空 previous/current/pending 与 provider/pipeline 引用，完成对象变成 generation-neutral。失败 rollback 仍在 Finish 前拥有完整 previous snapshot，原子性没有削弱。
- 测试：`RemovingAndRestoringPluginRenderingCommitsTheSameUnavailableStateAsColdStart` 在 request 中注入真实 collectible Plugin payload，并故意继续持有已完成 transaction，强制 compacting GC 后验证 retirement monitor 已完成；`RemovingUnusedRenderingPluginReleasesItsAssemblyContext` 覆盖无资源基线。真实临时 Project 运行中移除 Plugin 不崩溃，退出 GPU closure 正常。
- 关闭标准：提交后显示与冷启动相同的 Missing/unavailable；完成 transaction、Inspector、Scene cache、viewport 和 request queue 均不固定旧 ALC；相同 Stable ID 回归可重建。已满足。

### ARCH-045：Tree guide 为追求连续而穿过 disclosure row

- 复现证据：Hierarchy 的纵线与同层横线重叠，且 disclosure triangle 下方纵线过长，形成穿过行内容的视觉连接。
- 根因与影响：guide 起点被改为父行中心，并根据提交后的完整 content bounds 延伸；连续性目标覆盖了原本用于分离结构层级的行间空隙。
- 当前实现：恢复按当前帧 `TreeNode` 起点、文本行高和 item spacing 计算的 gapped geometry；child connector 从父行底部与子行顶部之间开始，绝不触碰父行中心，同时不改变原生 entry 高度。
- 测试：`TreeGuideSegmentsRemainVisibleAcrossCompactRows` 与 `ChildGuideRetainsAVisualGapBelowTheParentWithoutChangingCompactRowHeight` 验证连接线仍可见、父行中心无重叠且行高保持不变。
- 关闭标准：Tree line 不覆盖 triangle 或横线，不为修线增加 entry 高度。已满足。

### ARCH-046：无属性 GameBehavior/GameSystem 仍绘制空 Card body

- 复现证据：`Rendering2DSceneSystem` 只有 header，但下方仍出现一条空黑色正文区域。
- 根因与影响：Inspector 在 card 展开时无条件创建 `CardBody`；即使序列化属性集合为空，body 的 padding、background 和 border 仍占据高度。
- 当前实现：Component/System drawer 一次取得属性快照。Missing 类型显示状态说明；有属性类型绘制属性；无属性类型在标准 body 内显示淡色 assembly origin，避免没有信息的空黑区域。Header、折叠状态、enabled 和外部卡片间距始终一致。
- 验收：空类型只显示单行来源，不产生无内容留白；Missing 类型仍显示状态说明；有属性类型维持现有布局。
- 关闭标准：GameBehavior/GameSystem card 的正文必须包含可解释内容，不能只占据空白高度。已满足。

### ARCH-047：Rendering2D extraction 触发 nullable 泛型约束警告

- 复现证据：脚本编译对 `Camera2D?`、`SpriteRenderer2D?`、`TilemapRenderer2D?`、`Light2D?` 推断 `TComponent`，产生四条 `CS8631`。
- 根因与影响：`out T?` 与 nullable 局部变量共同参与泛型推断时，编译器可能把 `TComponent` 推断为 nullable annotated type，而 `TryGetComponent<TComponent>` 的约束要求非 nullable `GameComponent`。
- 当前实现：官方 2D Plugin 在四个调用点显式指定非 nullable泛型参数，例如 `TryGetComponent<Camera2D>(out Camera2D? camera)`；输出变量仍准确表达查找失败时为 null。
- 验收：真实 Project Plugin 编译不再包含对应 `CS8631`，行为与缓存索引不变。
- 关闭标准：不通过 suppression 或放宽泛型约束消除警告。已满足。

### ARCH-048：空显式依赖被推断为全部活动 Plugin，导致第一次卸载失败与重复错误

- 复现证据：运行中从文件系统删除 Plugin 后，场景已尝试切换 Missing，但 `INNO-ALC-UNLOAD` 报告 Plugin、RuntimeScripts、EditorScripts 仍可达；再次手动 Reload Plugins 后才恢复。Console 同时出现 coded Diagnostic 与内容重复的普通 Error log。
- 根因与影响：dump 的 GC root 证明旧 Plugin `Type` 由活动 `RuntimeScripts` 的 `ModuleLoadContext.m_sharedAssemblies` 持有。`AssemblyLoadRequest.upstreamModuleNames=[]` 本应表示无依赖，但 `ModuleHost.GetUpstreamModules` 将它解释为自动附加所有活动 Plugin；因此同一移除事务 stage 的新 RuntimeScripts 又抓住 previous Plugin assembly。重复错误来自 Editor 同时调用 Diagnostic publisher 和 Logger。
- 当前实现：所有 module domain 只接受显式 upstream dependency snapshot，空集合严格返回空；编译器仍负责生成完整 Plugin → Runtime Scripts → Editor Scripts DAG。reload 以 250 ms 间隔、3 秒上限按帧协作式验证 CLR unload，避免在单帧连续执行完整 GC。Editor 只发布唯一 `INNO-ALC-UNLOAD` Diagnostic，不再复制普通 Error。
- 测试：`PluginRemovalUnloadsTheCommittedMissingGenerationWithoutASecondReload` 使用带 Generated Serialization Converter 的真实 collectible Plugin，执行一次 refresh/compile/apply，验证活动图不含 Plugin 或指向其 Stable module name 的 upstream、Scene 转为保留 identity 的 Missing、旧 Component/Type 弱引用死亡且 unload verification 成功。
- 关闭标准：第一次自动 reload 即得到与缺失 Plugin 后冷启动一致的 Missing 状态；不需要第二次手动 reload；真实 retain 才产生单一 Diagnostic。已满足。

### ARCH-049：Inspector card 因属性数量改变交互模型

- 复现证据：无序列化属性的 `Rendering2DSceneSystem` 曾被特判为 leaf，三角与可展开正文消失；同一个类型新增属性后 card 才突然变为可展开。
- 根因与影响：把“当前没有属性”等同于“类型没有可解释正文”，使 card 交互受瞬时反射结果控制，也无法说明 System 来自内建程序集、Project Scripts 还是 Plugin generation。
- 当前实现：删除 content-free leaf 分支，所有 GameBehavior/GameSystem 都使用相同 `CollapsingCard` 契约。无属性正文显示 generation-neutral 的 domain、scope 与 assembly name；不缓存 collectible `Type` 或 delegate。
- 验收：无属性、有属性与 Missing card 都有同一 disclosure、缩进和 native tree scope；无属性来源采用 File Browser breadcrumb 的淡色样式。
- 关闭标准：card 交互不再随属性数量变化，且空类型有可验证的来源信息。已满足。

### ARCH-050：禁用 Rendering2DSceneSystem 后 scope 绕过生命周期重新填充缓存

- 复现证据：Inspector 将 `Rendering2DSceneSystem.enabled` 关闭后 `OnDisable` 已清空 extraction cache，但 Scene/Game View 仍继续显示 Sprite。
- 根因与影响：viewport/request provider 随后直接调用 internal `Capture()`；该方法无 activation guard，会立即从 Scene 重新建立 Camera、Drawable 与 Light 索引，实际绕过 `GameSystem.isActiveAndEnabled` 契约。
- 当前实现：extraction owner 自身在 `Capture()` 入口执行 `isActiveAndEnabled` 门禁；inactive 时幂等清空所有 Plugin Component 引用并返回共享空 snapshot。所有 Editor、Player 与显式 viewport 路径仍通过同一个 scope/capture 入口，不增加 UI 特判或第二套状态。
- 验收：禁用后 Scene View 只保留 Editor-owned grid/axes，Game View/Player 不再得到 Base Camera request；重新启用后按当前 Scene 结构重建；Plugin source 由真实 Editor scripting compilation 验证。
- 关闭标准：任何调用者都不能从 disabled/detached/destroyed system 取得可渲染 Scene 数据，且无需重建 Scene 或重启 Editor。已满足。

### ARCH-051：Viewport 失败文字越界且停留在左上角

- 复现证据：Scene/Game Provider 错误包含完整 Scene 名称或异常时，文字从左上角单行绘制并越过 Panel 右边界。
- 根因与影响：两个 Panel 各自直接调用 `AddText`/`TextUnformatted`，没有共享可用区域、padding、换行宽度或垂直布局语义。
- 当前实现：`ImGuiWidget.CenteredWrappedText` 在完整区域内计算 padded wrap width，将文本块整体居中并裁剪；Scene/Game unavailable 状态统一使用淡色文本和 `48 × 32` logical padding。
- 测试：`CenteredWrappedTextStaysInsidePaddingAndUsesMultipleLines` 检查 widget 占满区域、文字顶点位于 padding 内且确实生成多行。
- 关闭标准：任意长度状态文本不贴边、不越界，并在 Scene/Game View 中保持相同布局。已满足。

### ARCH-052：Game View 无法完整预览固定画幅

- 复现证据：Game Panel 改变宽高时直接改变 render target aspect，无法选择完整画面加黑边的预览模式。
- 根因与影响：Panel 尺寸被直接当成游戏画幅；若只在输出后遮罩，投影与 picking 仍使用错误 aspect。
- 当前实现：新增 Editor setting `Editor/Appearance/Viewports/Game Framing`，默认开启 `16:9`。Panel 计算最大内接矩形，以该矩形的真实像素尺寸提交 Provider，再居中显示并绘制纯黑 letterbox/pillarbox；关闭后使用完整 Panel。
- 验收：宽屏和竖向 Panel 都显示完整 target，不拉伸、不裁剪；Camera、Scene、Build Profile 与 Player 数据中不新增 Editor 预览字段。
- 关闭标准：画幅开关和比例由 Editor Settings 持有，render target 与最终显示 aspect 一致。已满足。

### ARCH-053：Camera2D 重复持有 Pixels Per Unit

- 复现证据：Project 2D Settings 与每个 `Camera2D` 同时保存 PPU，pixel-perfect 投影可能因 Camera 值不同而与 Sprite 默认密度分裂。
- 根因与影响：项目 authoring density 被误建模成 Camera 实例属性，产生两个 owner 和不明确优先级。
- 当前实现：移除 Camera 的 serialized PPU；pixel-perfect camera 和没有资源级覆盖的 Sprite 统一读取 `Rendering2DProjectSettings.defaultPixelsPerUnit`。Sprite 的 PPU 保留为不同图集密度的显式资源级覆盖。
- 验收：官方 Plugin runtime/editor scripts 均通过编译；Camera Inspector 不再出现 PPU；当前 Project Scene 与 Plugin Sample Scene 已由当前 writer 重存，Camera payload 不再含旧 key；项目 Settings 是 pixel-perfect Camera 默认密度的唯一 owner。
- 关闭标准：同一项目默认密度只有一个持久化来源，不添加旧字段读取或双路径。已满足。

### ARCH-054：Game Export 缺少 Support Pack 且 CLI 部署闭包不完整

- 复现证据：源码运行的 Editor 在 `AppContext.BaseDirectory/SupportPacks/macos-arm64` 找不到发行资产；生成 Pack 后，独立 Build CLI 又可能把已存在的 Startup Scene 判定为未导入。
- 根因与影响：Support Pack 尚未由引擎发行阶段安装；同时 `Inno.Scene.Assets` 只是 `Inno.Build` 的 implementation reference，不会自动成为 CLI 输出的传递 deployment closure。Build 还在等待 pending import 前验证 Scene type，存在竞态。
- 当前实现：Support Pack 继续由独立发行工具生成，Game Export 只消费 Pack、不运行 `dotnet`。Build CLI composition root 直接部署 `Inno.Scene.Assets`；Build 在验证 Startup Scene 前等待 Asset Pipeline idle，再捕获组合 snapshot。
- 测试：`GameBuildWaitsForPendingStartupSceneImportBeforeValidation` 覆盖 rescan 后立即构建；真实 `InnoProject` 使用 macOS ARM64 Pack 完成原子导出，并从 `.app` 内运行 Player。
- 关闭标准：预生成 Pack 安装后 Export 不访问源码或编译器，独立 CLI 可发现 Scene importer，pending source 不被提前判为无效。已满足。

### ARCH-055：导出 Player 未激活 Plugin/Scripting generation 导致紫屏

- 复现证据：真实 `InnoProject.app` 能初始化 Metal/BGFX 并正常运行 120 帧，但窗口保持整屏紫色；日志中没有 `s_spriteTexture`、2D vertex layout 或目标 shader program，也没有 rendering diagnostic。
- 根因与影响：Player 直接把 `Managed/*.dll` 加载到默认 ALC，却没有登记到 `ModuleHost`。默认 Host Catalog 按设计只发现 `InnoInternal`，因此 Plugin 与 Game Scripts 虽已进入进程，仍对 TypeCache、Serialization Registry 和 `RenderExtensionRegistry` 不可见；没有 request provider 时 BGFX 只提交空帧。
- 当前实现：Build 将 `ScriptCompilationResult.activationRequests` 的 runtime-only 模块拓扑写入 `GameRuntimeManifest`。Player 严格校验 Managed closure 后，通过 `ModuleHost.BeginReload` 依赖排序、collectible ALC 候选加载并原子激活整个冻结 generation，随后才创建 Settings、Session、Rendering 并反序列化 Scene。不扩大 Host 扫描、不使用默认 ALC fallback。
- 测试：Build tests 验证 RuntimeScripts module/domain/main assembly；Player E2E 要求 `INNO-SMOKE frames=3` render-loop 证据；真实项目重新生成 Support Pack 和 `.app` 后报告 `frames=120 views=1 draws=1`，出现 2D uniform、vertex layout、shader program 与 texture 创建并正常 shutdown。
- 关闭标准：导出 Player 的插件组件、脚本类型和渲染扩展属于同一显式 active generation；部署 DLL 与清单不一致时启动即失败；有效 2D 场景不再停留在后端空帧颜色。已满足。

### ARCH-056：Viewport 单模型排他使 2D 与未来 3D 无法同屏组合

- 复现证据：旧 Editor registry 对一个 viewport kind 只选取一个 Provider；2D Provider 又要求 content scope 中每个 Scene 都存在 `Rendering2DSceneSystem`。加入未来 3D Provider 后两者会争夺 Scene/Game View，纯 3D Scene 还会让整个 2D scope 失败。
- 根因与影响：Viewport 用途、渲染模型、交互控制者和 presentation target 所有权被压缩成一个对象。架构只能在 2D/3D 中二选一，无法表达 3D 底层、2D overlay、混合 Scene 或同一 scope 中不同模型 Scene。
- 当前实现：用 Attribute 驱动的 `EditorViewportContributor` generation 取代单 Provider。Host 收集所有适用 Contributor，按 order/Stable ID 冻结 `EditorViewportComposition`，每层提交到同一离屏 target；`controllerPriority` 独立选择唯一导航/工具控制者。Runtime 仅在成功建图后记录 target/viewport 覆盖区，后续重叠请求通过 `RenderPipelineContext.preservePresentationTarget` 使用 Load/Preserve；不相交区域仍可独立初始化。2D scope 跳过未选择 2D system 的 Scene，并以 order 1000 作为可叠加模型层。
- 测试：`OverlappingRequestsPreserveEarlierPresentationLayersInSchedulingOrder`、`DisjointViewportsCanInitializeTheSamePresentationTargetIndependently`、`FailedRequestDoesNotClaimThePresentationTarget` 覆盖运行时 presentation 所有权；`CompositionCanonicalizesModelLayersByOrderAndStableIdentity`、`CompositionSnapshotsTheCallerCollection`、`CompositionRejectsEmptyNullAndDuplicateModelLayers` 覆盖 Editor composition 稳定性；真实官方 2D Plugin 编译与 Metal Editor smoke 验证 Contributor 发现、target 提交和现有 2D 行为。
- 关闭标准：同一 viewport kind 可同时接受多个独立渲染模型；纯 3D、纯 2D 和选择多个模型的 Scene 可共存；失败层不破坏健康层；Editor/Rendering Core 不引入 2D/3D 世界观；旧 Provider API、转发和兼容包装为零。已满足。

## 2026-09-02 Viewport、Inspector、2D Settings 与 Game Export 验收

- `InnoEngine.sln` 完整构建：0 warnings、0 errors。
- `InnoEngine.sln` 完整测试：600 passed、0 failed、0 skipped。
- `Inno.Editor.Scripting.Tests` 定向测试：51 passed、0 failed，其中包含 viewport 居中换行与 padding 回归测试。
- `Inno.Tooling.Architecture`：通过，无依赖方向、可见性、XML 或禁止模式违规。
- 官方 `Inno.Rendering.2D` Plugin：当前 runtime/editor scripting diagnostics 均为空，不存在新增编译 warning 或 error。
- macOS Metal Editor 实机 smoke：运行 120 frames；完成 2D vertex layout、Sprite shader/resource 创建及 BGFX shutdown，进程正常退出，未出现 reload、RefCount 或 BGFX fatal diagnostic。
- 真实 `InnoProject` macOS ARM64 Export：重建 Release BGFX 与 Support Pack 后原子导出；Content 仅包含 catalog、content-addressed pack 与 runtime manifest；导出 Player 运行 120 frames 并干净退出，无 RefCount warning。
- macOS ARM64 Player E2E：创建独立 Project、验证 deployment forbidden closure、原子导出并实际启动 Player，完整通过。
- diff whitespace 校验：通过。

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

## 2026-09-02 Rendering、生命周期与 ALC 最终验收记录

- `InnoEngine.sln` Debug 全量构建：0 warnings、0 errors。
- `Inno.Tooling.Architecture`：通过；`git diff --check`：通过。
- 全仓测试：598 passed、0 failed、0 skipped。
- `GraphicsFacadeResolvesTheCurrentlyBoundRenderingRuntime`：两个 Render Runtime 的 façade、统计与 pipeline scope 完全隔离。
- `GameBehaviorLifecycle_ReindexesAfterStructuralChanges` 与 `GameBehaviorLifecycle_StartsLateOnlyBehaviorWithoutDispatchingUpdate`：结构索引和精确 phase mask 通过。
- `TextEditStateIsScopedByRendererOwnerAndPropertyPath`：Inspector 编辑状态按 renderer、owner 和 path 隔离。
- Rendering reload 测试故意继续持有已完成 transaction 并注入真实 collectible Plugin payload；compacting GC 后 Plugin ALC retirement monitor 完成。
- 当前 Project Scene 与官方 Plugin Sample Scene 均包含唯一 `Rendering2DSceneSystem`，稳定帧不再按 Camera 重扫 Scene。
- macOS ARM64 Debug Editor 真实 Metal 120-frame smoke：exit code 0，日志包含 `BGFX Shutdown complete`，且不包含 `RefCount is`、`INNO-ALC-UNLOAD`、BGFX Fatal、managed resource closure 或未处理异常。

## 2026-09-02 Tree、Inspector、Plugin warning 与单次卸载验收记录

- `InnoEngine.sln` Debug 全量构建：0 warnings、0 errors；`Inno.Tooling.Architecture` 与 `git diff --check` 通过。
- 全仓测试：599 passed、0 failed、0 skipped。
- Tree 两项几何回归验证 disclosure row 保留空隙且 entry 高度不变。
- `PluginRemovalUnloadsTheCommittedMissingGenerationWithoutASecondReload` 验证一次 refresh/compile/apply 后场景进入 Missing、活动依赖图不再包含被移除 Plugin，旧 Component/Type 与三层 collectible ALC closure 均可回收。
- 真实 InnoProject Plugin 编译产物的 `diagnostics.cache` 仅包含 8-byte 空诊断文档，不再产生四条 `CS8631`。
- macOS ARM64 Debug Editor 完成 600-frame baseline、运行中移除 Plugin 的 1800-frame smoke 以及恢复后的 600-frame smoke；三次均 exit code 0、最新 session log 为 0 byte、BGFX 正常 shutdown，无 `INNO-ALC-UNLOAD` 或重复普通 Error。

## 2026-09-02 多渲染模型 Viewport Composition 验收记录

- `Inno.Tooling.Architecture` 通过；`InnoEngine.sln` Debug 全量构建 0 warnings、0 errors；`git diff --check` 通过。
- 全仓测试：606 passed、0 failed、0 skipped；其中 Runtime 三项测试覆盖重叠层、非重叠区域和失败请求，Editor 三项测试覆盖稳定排序、调用方集合快照与非法 composition 拒绝。
- 删除派生 Plugin source cache 后启动真实 `InnoProject`，只生成一个当前 content-hash cache；新的 Game/Scene Contributor 均进入缓存，最新三层脚本诊断文档均为空。
- `InnoProject.sln` 只包含 `Inno.GameScripts` 与 `Inno.EditorScripts`，构建 0 warnings、0 errors；Project 根目录不生成 Plugin `.csproj`。
- macOS ARM64 Metal Editor 运行 120 frames，完成两个离屏 target、首帧和 BGFX shutdown，进程 exit code 0；本次启动区段无 reload、RefCount、Fatal 或未处理异常。
- 从当前源码重新生成 macOS ARM64 Support Pack；Player E2E 完成确定性 Content、forbidden closure、原子提交和真实 3-frame Metal 进程验证。
- Windows x64 仍按 ARCH-030 由 Windows CI runner 做平台进程验收；本次中立 composition 契约、构建和测试不包含平台分支。

## 台账维护规则

- 关闭问题必须同时更新实际测试名和最后验证结果。
- 仅添加日志、catch 或 fallback 不构成整改。
- 不允许通过兼容读取、旧 API 包装、friend assembly、测试反射或 public 后门关闭问题。
- 新问题使用新的稳定 ID，不复用已关闭编号。
