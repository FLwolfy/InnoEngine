# Inno.Assets.Pipeline

同步 Pipeline 修改与 loader 操作通过共享 `GenerationCoordinator.AcquireOperation` 保护完整调用范围。
Pipeline 在捕获 loader 前对账 dirty Catalog；进入操作后 Type/Serializer 查询引发的自动刷新只能延后，
不能在当前 loader 的调用栈中退休它自身。候选发布仅在自己的控制线程借用该保护，不放宽 GC 或 Faulted gate。
Catalog 文件提升在 journal 删除失败时先恢复旧 snapshot；外层 metadata stage 再逆序恢复已写 sidecar。
恢复失败聚合上抛并由 publication owner Fault，不能宣称成功。文件系统多文件操作仍是带补偿的安全点提交，
不是跨文件 crash-atomic 事务。

## Missing 与只读失败记录

Asset reference 和 dependency record 只按 persistent ID 解析；last-known path 仅作诊断。旧路径出现新 identity 时原引用保持 Missing。

Tombstone 保留中立 property bytes、依赖描述与 last-successful artifact key；释放 live payload/强引用，不将旧 artifact 视为当前可加载内容。Tombstone 的旧 key 不在活动 path 集合中，因此仍允许正常 CAS 回收；保留 key 不等于永久保留缓存文件。

只读源 metadata 不一致时，失败进入可写 Catalog；记录失败不会再次调用只读 source writer，也不会修改安装包。缺失 sidecar 或 identity 冲突仍明确拒绝，不提供 legacy metadata fallback。Source mount dependency set 和发布的 mount 列表为不可修改快照。

[Assets 索引](README.md) · [Runtime Assets](Inno.Assets.md) · [Plugins](../plugins/Inno.Plugins.Authoring.md)

## 职责与边界

该 authoring project 拥有 Source Mount、文件索引/watcher、Importer、Build Processor、`.imeta`、依赖图、Catalog、CAS Artifact、canonical authoring object 和 runtime closure export。Player 不引用它。

## 公开 API

| 分组 | 主要 API |
| --- | --- |
| Composition | `AssetPipeline`, `AssetPipelineOptions`, `AssetPipelineMode`, `AssetSourcePolicy`, `AssetCacheOptions` |
| Source | `AssetSourceMount`, `AssetSourceMountTransaction`, `AssetFileSystem`, `AssetFileEntry`, `AssetSample`, `AssetChangedEvent` |
| Import | `AssetImporter`, `AssetImporter<T>`, `AssetImporterExtensionAttribute`, `AssetImportContext`, `AssetImportWriter<T>`, `AssetImportHealthSnapshot`, `AssetImportFailure` |
| Build | `AssetBuildProcessor`, `AssetBuildProcessor<T>`, `AssetBuildProcessorExtensionAttribute`, `AssetBuildContext<T>`, `AssetArtifactWriter` |
| Transactions/export | `AssetCatalogCandidate`, `AssetExportContext`, `AssetSerializationServices`, `AssetDeploymentScope`, `NativeAssetSourceSerialization` |
| Script authoring | `EditorAssets` 提供当前 Editor Session 中显式受限的保存入口；不暴露 Pipeline owner 或 mutation graph。 |
| Advanced facade | `AssetLoader`，用于独立 authoring host；普通 Application 优先使用 `AssetPipeline` |

Plugin Importer 使用 `InnoEditor.Assets` 中的 Attribute、Importer base、Context、Writer 与
`NativeAssetSourceSerialization`。原生结构化 Source helper 只接收 Context 提供的
`AssetSerializationServices`，不会向 Plugin 泄漏 `TypeCatalog`、`SerializationRegistry` 或候选
Asset resolver。Importer 源码属于 Plugin 的 Editor assembly，不能进入 Runtime Plugin assembly。

## 组合与生命周期

```csharp
using var assets = new AssetPipeline(
    modules,
    types,
    serialization,
    identities,
    diagnostics,
    logs,
    AssetPipelineOptions.Create(assetRoot, libraryRoot));

assets.Update();
TextAsset value = assets.Load<TextAsset>(AssetPath.Project("Config/value.txt"));
```

所有 mutation 必须在构造线程执行。Save、Import、ImportSample、Move、Delete、CreateDirectory 和 source candidate commit 各自发布一个 revision；后台 `ExportRuntimeArtifactsAsync` 只使用 owner thread 捕获的 immutable Serialization generation。

### Source Mount 候选与共同 Recovery

`PrepareSourceMounts` 返回未发布的 `AssetSourceMountTransaction`。`sourceMounts`、
`GetFileSystemEntries`、`TryGetInfo`、`TryGetArtifact` 和 `Load<TAsset>` 只查询候选。
候选 canonical object 已有 persistent ID，但激活前没有 runtime ID，不能占用活动对象的 Identity 注册。

`Activate` 使用共享 `ReferenceRecoveryTransaction` 发布新 loader/文件索引的 Identity，
随后解析先前已加载资产与 tombstone 的中立引用槽。`recoveryChanges` 是只读、无对象强引用的
`ReferenceRecoveryChange` 列表：激活前/回滚后为空，激活后提供真实 Resolved/Missing 结果。
这个入口只供 Host 诊断与事务组合，不导出到游戏脚本。损坏的 Imported canonical recovery 拒绝候选；
真正暂缺的资产保留同一 ID、type、路径提示与 property bytes。原 source 和原 `.imeta` 返回后可恢复；
同路径新建文件但没有原 metadata 不视为同一资产。

`Complete` 提升 catalog 并退休旧 loader；`Rollback` 恢复未被修改的旧 canonical object，再退休候选。
两者均复用 Core `LifetimeScope` 与 `RetirementBarrier`，退出真正完成前不清空候选或旧 owner。
短暂 Pending 在 owner 线程驱动至完成；超时保留两代所有权并 Fault 共享 generation gate，
后续访问与依赖销毁均拒绝，必须重启 Host。普通退休错误仍尝试其余可释放资源并明确报告。

独立使用 `AssetLoader.Dispose` 时，它只报告 Pending，不同步阻塞正在执行/排队的操作；
Host 应保留 loader 并在安全点重试。停止接收新工作后，已准入操作仍可完成；
canonical 卸载 hook、Identity 注册、registry 和诊断按依赖顺序退休，包含仅留在 ID 索引中的 tombstone。

Assembly Catalog 更换 Importer/type 时也复用这一隔离候选：Prepare 冻结原有可写 import failure 指纹，
Activate 在候选 Type/Serializer 生效后只刷新候选 loader、校验新增失败，再发布其 Identity/recovery。
失败直接恢复未修改的旧 loader，不再标记“下次访问重新 Rescan”。首次注册 participant 不重复发布已初始化的源。
如果 Plugin 编译已准备 Source Mount candidate，则程序集事务只加入该候选进行验证，不创建第二份候选，也不
抢占 Plugin 原有的 Activate/Complete/Rollback 所有权。Settings/Graph/Plugin availability 全域 Recovery 仍需继续迁移。

候选的 `.imeta` 读写进入内部 `AssetSourceMetadataStage`，包含创建、目录 metadata、重定位和删除；
Prepare/Activate 不改创作源 sidecar。Complete 先验证所有已读取 metadata 的原始 bytes 是否仍匹配，
再写入差异并提升 Catalog；其中任何一步失败会逆序恢复已经写入的 sidecar，保留两种错误并 Fault。
外部修改冲突不会覆盖外部新数据。未提交候选不能 Save 创作源；正常已提交 loader 仍使用原本的可写 API。
这提供 owner-safe-point 事务及失败补偿，不声称文件系统支持跨多个文件的单条原子指令。

候选的 import/build/reference/catalog 诊断只暂存中立数据，激活时才创建 Core `DiagnosticReporter`；
停用时撤销当前 reporter，但保留回滚所需的中立报告。旧代 Dispose 不清除新代报告，回滚会恢复旧未解决诊断。
领域并未新增第二个 DiagnosticHub 或 sink。

## `~` 开发目录与安装态 `.isample`

Project Source Mount 中，名称以 `~` 开头的目录在 File Browser 中显示为 `ISAMPLE`，但仍使用普通创作语义：Asset Import、Catalog、Artifact、authoring 脚本编译、Editor 运行和 Play Mode 都会正常处理，完整 Project 导出为 `.iplugin` 时也会携带这些源文件。`AssetSample.HasSampleDirectoryName(path)` 只表达这个与 Source 无关的命名/显示分类，不表示该目录需要导入。`AssetSample.IsRuntimeExcluded(path, isDirectory)` 则统一表达 deployment 边界：Game 的 runtime Asset 与 runtime script closure 始终剔除任何 Source 下的 `~` 子树；普通 runtime Asset 若依赖其中内容，导出会因闭包不完整而明确失败，Startup Scene 位于其中时也会被明确拒绝。

只读 Plugin Source Mount 中，名称以 `~` 开头的目录才是逻辑 `.isample`。`AssetFileSystem` 仍索引目录及后代，`AssetFileEntry.isSample` 标记该目录本身，`isSampleContent` 标记完整子树；Editor 可以浏览它们，但 Asset Import/Catalog、Artifact 与 Plugin 脚本编译不会处理该子树。

`AssetPipeline.ImportSample(source)` 把选中的安装态 `.isample` 原子复制到 Project `Assets` 根，并完整保留所选根目录名及其所有前导 `~`。复制保留 `.imeta`，所以 Sample 内部 persistent reference 不会因导入断裂；`.abin` 与 source noise 不复制。事务拒绝 Project 自身的 `~` 目录、符号链接、目标冲突和复制期间发生变化的 Source，失败时不会留下半个目标。成功后 `~` 目录立刻按 Project 普通 authoring content 进入 Asset import 与脚本 generation，但仍不会进入 runtime deployment。

损坏当前格式、只读 mount 写入、Importer 冲突、Artifact closure 不完整和 observer failure 都明确报告。`Library` 可删除重建，不作为创作事实来源。
