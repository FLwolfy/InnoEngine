# Inno.Assets

## 冷加载峰值预算与合并

`AssetDatabase` 提供构造参数 `preparationBudgetBytes`（默认 64 MiB）和只读 `preparingBytes`。这是 encoded IO 预留预算，不等同于已有的 resident payload 预算。同一数据库内相同 persistent ID 的并发请求共用一次依赖闭包读取；不同 root 引用相同 dependency 时也共用该不可变 payload 的 IO 和唯一字节预留。取消一个等待者不取消其他等待者。

在开始读取前对闭包中尚未预留的 payload 长度求和并预留预算。打开文件后先验证实际长度，再分配精确大小并校验 hash；准备完成但尚未经过 `CompletePendingLoads` 的 bytes 仍计入预留。最后一个使用该 payload 的 root 完成/取消后归还预留。并发请求数仍限制为 32，超预算明确拒绝，不静默转为同步加载。

`preparationStatistics` 返回不可变 `AssetPreparationStatistics`：`pendingRequests`、`peakPendingRequests`、
`rejectedRequests`、`pendingPayloads`、`payloadReadsStarted`、`sharedPayloadReads`、`reservedBytes`、`peakReservedBytes`。
累计/峰值计数的范围是数据库生命周期；当前计数在 publication/cancellation 后下降。`sharedPayloadReads`
专指不同 root 的共享，重复请求同一 root 不会重复建立其依赖读集合。该统计不持有 Asset、Task、Type 或 delegate。
每个数据库最多同时进行 4 个 payload 文件读取；共享不会变成无上限同时打开文件。Dispose 先取消并排空全部 IO，再销毁读取 gate。

`IAssetReferenceResolver` 的通用引用桥会拒绝不同 persistent ID 的返回值，不允许 last-known path 偷换目标。

运行时外部 owner（例如 Settings）解析合法但尚未物化的引用时，AssetDatabase 通过同一 canonical
冷加载路径解析，并采用 Session pin。资产自身物化仍使用内部的 prepared-only resolver 视图，
未在预备依赖闭包中的引用不能偷偷触发第二次冷加载。两者复用同一 `IAssetReferenceResolver` 协议与
Identity/type 校验，不增加持久化引用格式。通用引用桥必须原样传播 `RetirementPendingException`，
不得将未退出的 generation 包装成普通 Missing。

[Assets 索引](README.md) · [Assets Pipeline](Inno.Assets.Pipeline.md) · [Wiki 首页](../README.md)

`Inno.Assets` 是 Player-safe 的运行时资产契约和只读 `AssetDatabase`。创作源、Importer、Watcher、依赖图和 Artifact Writer 属于 `Inno.Assets.Pipeline`；Editor 通过一个显式 `AssetPipeline` 实例组合它们。

## 运行状态与租约边界

`AssetRuntimeOwner` 是数据库私有持有的写入权限：Initialize 首次声明实例归属，后续 Initialize/UpdateAssetPath/Release/GetSourceHash 都验证同一 owner。它不保留资产列表，不是静态全局写入口，也不导出给脚本。跨 owner 写入明确失败。Asset 不在 finalizer 中执行派生回调；数据库显式 Release。payload hook 失败会恢复先前的字段、payload 与内容 revision。

`AssetObject.OnUnloading` 在资源真正退出后才算完成。派生 hook 抛出 `RetirementPendingException` 时，
`AssetRuntimeOwner.Release` 保留 payload 与未完成状态；owner 必须保留对象并重试，派生实现不得重复
已经完成的释放步骤。普通异常是终止失败，向上报告而不重复调用 hook；成功后重复 Release 无副作用。
`AssetLoader` 与 Player `AssetDatabase` 的最终退出均以 Core lifetime 保留 Pending canonical object、
Identity 注册与下层依赖，不能提前标记 disposed。进入退出后拒绝新加载。
Runtime budget eviction 也在卸载成功后才移除 dependency retention 与 residency 计数；
尚在退休中的记录不会被当作可继续使用的 loaded object 返回。

`AssetReferenceLocation` / `AssetReferenceInfo` 的公开构造用于创建中立诊断快照；后者复制并冻结 reference 集合，不授予修改资产的权限。

`IAssetArtifactLookup.AcquireArtifact(id, output)` 返回必须释放的 `ArtifactLease`。`ArtifactRetention.Retain(info)` 与 `GetRetainedKeys()` 为自定义 provider 提供 artifact-key 引用计数；provider 必须串行化 acquisition 与 collection。Authoring collector 把租约持有的旧 key 加入 reachable 集合，即使源已 reimport/delete 也不会提前删除。

`AssetLease<T>` 和 `ArtifactLease` 使用同一个内部 residency owner：直接或任意包装层内的 `RetirementPendingException`
保留 value 与 release callback，所属生命周期必须在安全点重试；成功或普通终止异常才撤销 value/callback。
同一租约的并发或重入释放不会同时执行 callback，而是返回 Pending，不能当成已经释放。回调在内部锁之外执行；
provider 对部分完成的引用计数、native 释放等必须记录阶段，重试不得重复副作用。终止之后再次 Dispose 无副作用。
包装 Pending 中的普通错误通过 Core 分类收集，按实例去重；之后即使 release 成功也以 AggregateException 报告。
单次普通终止失败仍原样上抛，不为了统一内部结构改变 provider 原来的异常类型。
`RetentionScope` 复用 Core `LifetimeScope` 的逆序退休：Pending 阻止下层依赖被提前释放，之前的普通失败保留到
最终聚合；开始退休之后禁止继续 Retain。它不是独立的第二套生命周期协议。

冷路径 `AssetDatabase.AcquireAsync` 在后台读取并校验不可变 payload，不把 Type、serializer、Identity 或 execution scope
流入 IO task。数据库固定接收 `TypeCacheSnapshot`，不在 Player 加载期间刷新 Authoring Registry。
`CompletePendingLoads(completionBudget = 8)` 由 RuntimeSession 每帧调用，在 owner thread 创建、反序列化和注册资产。
独立 Host 必须自己在安全点调用这一入口，不可在 owner thread 阻塞等待尚未发布的 lease。
最多同时准备 32 个请求，超限明确拒绝；取消或 Dispose 不发布半成品，销毁数据库会取消并等待其 IO 退出。
缓存命中返回已完成 lease；无论命中与否，调用方都必须 Dispose 租约。

## 初始化与目录

```csharp
using EngineHost host = new EngineHostBuilder().Build();
var identities = new IdentityAllocator();
using var assets = new AssetPipeline(
    host.modules,
    host.types,
    host.serialization,
    identities,
    host.diagnostics,
    host.logs,
    AssetPipelineOptions.Create(
        Path.Combine(projectRoot, "Assets"),
        Path.Combine(projectRoot, "Library")));
```

`AssetPipelineOptions`：

| 属性 | 说明 |
| --- | --- |
| `mode` | `Authoring` 对账可写 source 并生成 Artifact；`RuntimeArtifacts` 只信任部署 Catalog/CAS。 |
| `assetRoot` | 可写 Project Source Mount 根目录。 |
| `libraryRoot` | 可重建的 Project Library 根目录。 |
| `sourceMounts` | Project 与已激活 Plugin 的完整 mount 候选。 |
| `enableFileSystemWatcher` | 是否观察外部文件系统变更。 |
| `fileWatcherFlushDelayMs` | raw event quiet/debounce 窗口。 |
| `sourcePolicy` | 统一 Source ignore policy。 |
| `cacheOptions` | CAS 最大容量和 unreachable grace period。 |

`artifactRoot` 不再是 option。`AssetPipeline.artifactRoot` 是只读派生值 `<libraryRoot>/Artifacts`。`AssetCacheOptions.CreateDefault()` 当前使用 4 GiB 上限和 7 天 grace period。

## Owner-thread 模型

`Initialize` 记录当前 managed thread。以下 mutation 必须从该线程调用：

- `Update`
- `WaitForIdle`
- `Import`
- `Save`
- `Move`
- `Delete`
- `CreateDirectory`
- `Rescan`
- `BuildAsync`
- `ExportRuntimeArtifacts`

Editor Application 在每帧开始调用自己拥有的 `AssetPipeline.Update()`。Watcher callback 只 enqueue；`Update` 才 poll、对账、commit 和发布 observer。

```csharp
while (running)
{
    assets.Update();
    // Run frame work against one committed asset snapshot.
}
```

`WaitForIdle()` 适合测试、batch 工具和需要同步等待 source quiet window 的命令。它不是普通 frame loop 的替代品。

## 状态与事件

| 成员 | 说明 |
| --- | --- |
| `isInitialized` | 服务是否可用。 |
| `assetRoot` / `libraryRoot` / `artifactRoot` | 初始化后的绝对路径。 |
| `sourceMounts` | 当前原子发布的 Project/Plugin mount snapshot。 |
| `SourceMountsChanged` | mount generation 成功替换后的 owner-thread 通知。 |
| `Changed` | 一次 commit 后的 `AssetChangeSet`。 |
| `AssetReloaded` | loaded canonical asset 已原位更新。 |

Observer 按订阅顺序在 owner thread 调用。某个 observer 抛异常会被隔离，不能回滚已经提交的 transaction，也不会阻止后续 observer。
脚本 generation 切换后的第一次 `Update`/`Rescan` 会移除 `Changed` 与 `AssetReloaded` 中声明类型或 target 类型已经退休的 collectible observer，防止静态事件反向保留旧 ALC。Host observer 和当前活动 generation 的 observer 不受影响。

## 加载与保存

| API | 行为 |
| --- | --- |
| `IAssetLookup` | Authoring `AssetPipeline` 与 Player `AssetDatabase` 共同实现的最小只读查询边界。 |
| `IAssetArtifactLookup` | 两种宿主共同实现的 named Artifact 定位边界；Runtime 服务不需要依赖 authoring pipeline。 |
| `Assets.Load/TryLoad` | 项目脚本使用的无状态门面；只解析当前异步执行作用域，不拥有 Catalog、缓存或 Session 状态。 |
| `Assets.AcquireAsync<T>` | 异步取得 canonical asset 与显式强驻留 `AssetLease<T>`；释放 lease 后对象才可参与预算回收。 |
| `Assets.AcquireArtifact` | 取得 named immutable Artifact 的 `ArtifactLease`，并通过 `OpenRead()` 打开只读流。 |
| `RetentionScope` | 组合多个 lease，并按加入顺序的逆序统一释放。 |
| `Assets.LocalPath(localPath)` | 根据调用脚本 assembly 的 `Inno.AssetSource` metadata 创建 source-local 路径；同一代码在 Project 开发态与 `.iplugin` 安装态自动指向各自 Assets 根。 |
| `Load<T>(AssetPath/id)` | 返回跨 mount canonical instance；缺失或类型不兼容时抛异常。字符串重载表示 Project mount。 |
| `TryLoad<T>(path/id,out asset)` | 安全失败。 |
| `LoadAsync<T>(path/id,token)` | 在 worker 上执行真实加载/导入等待；相同 path 或 ID 的并发请求共享任务并返回同一 canonical instance。取消只终止当前调用者的等待，不取消其他调用者共享的加载。 |
| `Import(path)` | 显式导入单一受支持 source。 |
| `Save(asset)` | 导出到现有 source path。 |
| `Save(path,asset)` | 为新资产建立初始 source identity。 |
| `Move(oldPath,newPath)` | 仅在可写 mount 内事务式移动 source 与 `.imeta`，保留 persistent ID、canonical instance 和 artifact。 |
| `Delete(path)` | 事务式删除 file/directory source 与 sidecar；释放路径并保留 ID tombstone。 |
| `CreateDirectory(path)` | 创建带稳定 `.imeta` 的 source folder；folder 不生成 artifact。 |
| `Rescan()` | 对账全部 source/meta/catalog/artifact。 |

初始化会自动 `Rescan`，无需为已有文件逐个调用 `Import`。

Editor、Play Mode 与 Player 的 Composition Root 使用 `AssetExecutionContext.EnterScope(IAssetLookup)`
绑定当前 Session。Scope 严格按 LIFO 释放，并通过 `AsyncLocal` 隔离并行异步执行流；没有活动
Scope 时，脚本 `Assets` 门面明确抛出 `InvalidOperationException`。引擎内部服务始终直接依赖
`IAssetLookup`、`AssetPipeline` 或 `AssetDatabase`，不反向调用脚本门面。

异步加载捕获调用时的 Loader generation；Mount/Registry 原子切换不会让任务读到一半新、一半旧的 Catalog。`AssetPipeline.Dispose`/generation 退休会先拒绝新请求，再等待已经接受的加载结束后释放底层 Loader，因此后台任务不会访问已释放状态。最终 canonical cache 的发布仍由 Loader 自身的事务锁保护，不要求调用者切回 owner thread。

`Rescan` 同时是 TypeCache generation 的资源收敛安全点。若已加载 canonical asset 的运行时类型已退休，Loader 会从内部 record、identity 和 dependency retention 中释放它；仍存活的 host asset 会用当前 generation 重新恢复其序列化引用。调用方自己仍强持有旧 canonical instance 时，旧 collectible ALC 会按普通 CLR 引用规则延迟卸载，这不影响 Loader 返回当前 generation 的新实例。

`AssetPipeline` 也是统一 Assembly Catalog transaction participant。候选 TypeCache 与 Importer/Build Processor
Registry 激活后，它只对隔离 loader 的 Source Mount 重新对账，成功后通过共同 Source recovery 发布新 canonical
实例，旧 loader 直到 Complete 都不被 Rescan 修改；普通源文件 reimport 的原位更新与这种代际替换是两种不同操作。
已有 Plugin compilation candidate 保持原 publication owner，程序集事务只复用验证，不重复 Complete/Rollback。
激活前按 source path、状态、source hash、Importer ID 与结构化诊断记录可写 Project Mount 已有失败指纹；
候选新制造或改变失败时拒绝整个程序集候选。MVID 本身不属于失败语义，完全相同的既有失败保留为诊断而不阻塞 reload。
候选 sidecar 写入与诊断也暂存；失败恢复旧 loader/Identity/诊断，不通过下一次 GetLoader 延迟重扫修复。
相关提交与 metadata 外部冲突约束见 [Pipeline 候选协议](Inno.Assets.Pipeline.md)。

若本 transaction 或后续 participant 失败，ModuleHost 先恢复旧 TypeCache，AssetPipeline 再在下一次 owner-thread `Update`/访问时用旧 generation 自动恢复目录快照。Plugin mount 指向 `Library/Plugins/<pluginId>/<contentHash>` 中完整且不可变的 `.iplugin` generation snapshot，因此即使原安装包在 reload 中途被删除或覆盖，旧 Loader 的恢复、查询与关闭也不会访问失效路径。外部观察者不会看到“新 Registry + 旧 Asset Catalog”的半切换状态。

## Catalog 与 artifact 查询

| API | 说明 |
| --- | --- |
| `TryGetInfo(path/id,out info)` | 读取 immutable `AssetInfo`。 |
| `TryGetArtifact(id,outputName,out info)` | 定位 named output。 |
| `TryGetPersistentId(path,out id)` | 不加载 runtime object 查询 source identity。 |
| `TryGetAssetType(path,out type)` | 从 Catalog/Importer 解析类型。 |
| `BuildAsync(definition,inputs,token)` | 调用自动发现的 aggregate Build Processor。 |
| `ExportRuntimeArtifacts(destination)` | 写出裁剪 runtime Catalog、空 source 身份根与精确 CAS closure；不复制创作源。 |
| `GetDependencies(asset,recursive)` | runtime dependency graph。 |
| `GetReferenceInfo(asset)` | engine-known reference diagnostics。 |
| `GetLoadedPaths()` | 当前 canonical cache 的 `AssetPath` snapshot。 |
| `UnloadUnusedAssets()` | 协作式释放无外部 managed root 的实例。 |

`AssetPath(source, "")` 是合法的 Source Mount 根路径，可用于 `TryGetFileSystemEntry` 与 FileBrowser 导航，但它不是 Catalog 中的可导入资产。`TryGetInfo`、`TryGetPersistentId` 和 `TryGetAssetType` 对此类根路径稳定返回 `false`，不会把正常的 Assets/Plugins overview 查询转换成异常。

```csharp
if (AssetPipeline.TryGetInfo(AssetPath.Project("Scripts/Player.cs"), out AssetInfo? script) &&
    script.status == AssetImportStatus.Imported &&
    AssetPipeline.TryGetArtifact(script.persistentId, "source", out AssetArtifactInfo? source))
{
    Console.WriteLine(source.absolutePath);
}
```

## Source 文件树

`GetFileSystemEntries`、`GetFileSystemChildren` 和 `TryGetFileSystemEntry` 返回所有活动 mount 的统一 Source Policy 视图。条目的 `assetPath.source` 保持来源隔离；`.imeta`、artifact、IDE cache 和默认系统噪声不出现，Unsupported source 仍出现。

`PrepareSourceMounts` 返回隔离的 `AssetSourceMountTransaction`。候选拥有自己的 Loader、FileSystem、Catalog 暂存文件与查询入口；在 `Activate` 前不会改变 `AssetPipeline.sourceMounts`、普通加载结果或正式 `Library/AssetDatabase/Catalog.snapshot`。内容寻址 Artifact 可以安全复用正式缓存，但 Catalog 只有 `Complete` 时才执行单次 atomic replace；`Rollback` 删除暂存 Catalog，进程异常遗留的候选目录会在下次初始化清理。`Activate` 只做安全点内的临时切换，不释放旧 generation，也不通知观察者；`Complete` 才发布 `SourceMountsChanged` 并退休旧 generation。`ReplaceSourceMounts` 是立即执行 Prepare → Activate → Complete 的便利入口。

Source entry 也是正式 Identity 对象。存在 `.imeta` 时使用 source persistent ID 派生 entry ID；mount root、
目录或尚无 metadata 的节点使用隔离 `AssetPath` 派生的确定性 ID。候选 FileSystem 只初始化 persistent ID，
在 `Activate` 前不注册 runtime ID；激活时先停用 last-good entry，再原子注册候选，失败或 `Rollback` 时反向
恢复。因而同一 persistent identity 不会同时对应两个 live source entry，Editor DragDrop 也不会看到候选对象。

Plugin mount 必须只读，且只能来自完整 `.iplugin` 文件；Folder 和 `.zip` 安装不会创建 Mount。运行时和 Editor 无法绕过 source transaction 直接写入安装源或活动 snapshot。外部 package 变化只会触发 Plugin 候选事务。跨 mount 依赖必须由 mount 的 `dependencySourceIds` 明确授权。任何 Persistent ID 冲突或未声明依赖都会拒绝候选并保留旧 snapshot。该两阶段协议也允许 `.iplugin` 脚本从隔离候选 artifact 编译，而 File Browser、运行时资产与当前 Plugin Catalog 始终只观察 last-good generation。

FileBrowser List 使用 `AssetFileEntry.nameWithoutExtension` 显示名字，Grid 保持完整 `name`。所有实际命令始终使用完整 `assetPath`；公开 Manager/Loader/FileSystem 寻址 API 不再接受裸字符串路径。

## 外部 rename/delete/recovery

### source-only rename

当操作系统只移动 source、没有移动 `.imeta`：

1. native rename old/new path 优先关联；
2. Loader 检查目标 meta identity 冲突；
3. `.imeta` 无 overwrite 地移动到新路径；
4. Catalog path 与 loaded canonical `assetPath` 原位更新；
5. CAS artifact 不移动，内容不变时 key 不变；
6. 发布带 ID、oldPath、newPath 的 `Moved`。

平台若只报告 delete+create，Loader 会在提交删除前按缺失 record 和 fingerprint 做唯一匹配。存在歧义时不会猜测：新 source 获得新 ID，旧 ID 各自进入 tombstone，并在新记录上保留明确 diagnostic。

Watcher 增量刷新失败时会立即尝试 full rescan。第一次失败和 recovery 失败都是需要保留的 Log 事件；只有两条路径都失败、Source Database 仍处于不一致状态时才发布 `Asset Source Database` Diagnostic。后续任一成功 refresh/rescan 会自动清除该状态。

FileBrowser 的 New Folder、Rename 和 Delete 全部调用 AssetPipeline 的事务 API。`Move` 会暂停 watcher、提交单一 `Moved` change，然后恢复 watcher；目标 source 或 `.imeta` 已存在时明确失败，不进行覆盖。

### delete/recovery

- watcher quiet window 内出现 delete+create：折叠为 `Modified`，保留 ID，不产生短暂 Missing。
- quiet window 后确认 source 已删除：自动删除孤立 `.imeta`，旧路径立即释放；Catalog 仅按 ID 保留最小 tombstone，current/last-successful artifact 引用立即清空。
- 同路径稍后重新创建但没有原 `.imeta`：视为新资产并生成新 ID，不会误继承已删除资产的身份。
- source 与原 `.imeta` 一起恢复：在 ID 无冲突时可以重新采用原 identity；若旧 canonical 仍存在则原位恢复。
- source 和 `.imeta` 同时删除：与确认后的 source-only delete 结果一致。
- duplicate source + meta：已知原路径保留 ID，新副本获得新 ID，避免两个路径争用同一身份。

## CAS 回收

AssetPipeline 启动、`WaitForIdle` 和低频 idle update 会回收不可达 bundle。确认删除会立即解除 Catalog 中的 current/last-successful 引用；物理 bundle 仍由 reachability、共享引用、size limit 和 grace period 决定何时删除。超出 size limit 时优先删除最旧不可达 bundle；正常情况下尊重 grace period。

CAS 目录只占磁盘，不会因存在就常驻运行内存。Artifact key 与 Assembly runtime generation 都不会写入 Scene/Prefab schema。

## Runtime Artifact 模式

Game Build 在 Authoring `AssetPipeline` 上导出冻结的 runtime closure。目标目录必须为空。只有 Importer 明确声明 `AssetDeploymentScope.Runtime` 的记录进入部署 Catalog；这些记录必须同时拥有完整 `asset-state` 与 `runtime` output，且 runtime dependency 闭包中不能出现 `AuthoringOnly` Asset，否则整个导出失败。`.cs`、`.iasmdef` 等编译输入保留在 Authoring Catalog，编译结果由 Runtime DLL 部署，不复制源文件。结果只包含部署 Catalog 所引用的 bundle。

部署宿主通过 `RuntimeSession` 创建只读 `AssetDatabase`：

```csharp
using RuntimeSession player = host.CreateSession(new RuntimeSessionOptions
{
    kind = RuntimeSessionKind.Player,
    applicationId = applicationId,
    runtimeContentDirectory = materializedContentRoot,
    persistentDataDirectory = persistentRoot
});
```

`AssetDatabase` 启动时直接加载并严格验证部署 Catalog/CAS，不扫描 source、不引用 Assets Pipeline、不运行 Importer、不生成 `.imeta`、不回收部署 bundle，也不启动 watcher。Player 因而不具备任何 authoring mutation API，不会在用户机器上悄悄重建内容。

`AssetReferenceProtocol` 定义 Assets 在共享 Reference Catalog 中的稳定 kind；`IAssetReferenceResolver` 是该
协议的领域 resolver。所有 Asset-aware 序列化必须由 owner 使用 `AssetSerializationContext.Create(...)`
组合完整 context，不能由 converter、Scene 或 Editor feature 私自创建不完整 resolver scope。

Runtime Database 将旧的 `Load/TryLoad` 视为 Session 级 pin；显式 `AcquireAsync` 使用引用计数 lease。超过 `RuntimeSessionOptions.assetResidencyBudgetBytes` 时，只在安全访问点按 LRU 释放无 pin、无 lease、且未被其他已加载资产依赖的 payload。`AssetResidencyStatistics` 区分预算、resident bytes、materialized count 与 leased count；Artifact 文件属于冻结部署内容，不因对象 lease 释放而被删除。

最后一个租约释放触发预算回收时，`OnUnloading` 的 Pending 会保留该租约、canonical Identity 与 payload。
重试继续预算回收，但每个租约的计数只递减一次；不能提前归零后丢掉释放路径，也不能误减其他租约。

## 关闭顺序

Editor 先 Dispose 自己拥有的 `AssetPipeline`，再 Dispose Edit/Play Session，最后 Dispose `EngineHost`。Player 先释放 `RuntimeSession`，再释放 `EngineHost`。所有权由实例构造关系表达，不存在静态 Shutdown 顺序。
