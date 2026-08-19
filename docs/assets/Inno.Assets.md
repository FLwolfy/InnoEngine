# Inno.Assets

[Assets 索引](README.md) · [下一页：Assets.Core](Inno.Assets.Core.md) · [Wiki 首页](../README.md)

`Inno.Assets` 是应用级门面。它组合 Source index、Loader、Serialization resolver 与缓存策略，并规定所有 Catalog/canonical object/public observer 变更只能在初始化线程提交。

## 初始化与目录

```csharp
IdentityManager.Initialize();
AssemblyManager.Initialize(assemblyOptions);
TypeCacheManager.Initialize();
SerializationManager.Initialize();

AssetManager.Initialize(AssetManagerOptions.Create(
    Path.Combine(projectRoot, "Assets"),
    Path.Combine(projectRoot, "Library")));
```

`AssetManagerOptions`：

| 属性 | 说明 |
| --- | --- |
| `assetRoot` | 唯一 Source Database 根目录。 |
| `libraryRoot` | 可重建的 Project Library 根目录。 |
| `enableFileSystemWatcher` | 是否观察外部文件系统变更。 |
| `fileWatcherFlushDelayMs` | raw event quiet/debounce 窗口。 |
| `sourcePolicy` | 统一 Source ignore policy。 |
| `cacheOptions` | CAS 最大容量和 unreachable grace period。 |

`artifactRoot` 不再是 option。`AssetManager.artifactRoot` 是只读派生值 `<libraryRoot>/Artifacts`。`AssetCacheOptions.CreateDefault()` 当前使用 4 GiB 上限和 7 天 grace period。

## Owner-thread 模型

`Initialize` 记录当前 managed thread。以下 mutation 必须从该线程调用：

- `Update`
- `WaitForIdle`
- `Import`
- `Save`
- `Rescan`
- Manager 的 `LoadAsync` 提交阶段
- `BuildAsync`

`Shell.Tick()` 在每帧开始调用 `AssetManager.Update()`。Watcher callback 只 enqueue；`Update` 才 poll、对账、commit 和发布 observer。

```csharp
while (running)
{
    AssetManager.Update();
    // Run frame work against one committed asset snapshot.
}
```

`WaitForIdle()` 适合测试、batch 工具和需要同步等待 source quiet window 的命令。它不是普通 frame loop 的替代品。

## 状态与事件

| 成员 | 说明 |
| --- | --- |
| `isInitialized` | 服务是否可用。 |
| `assetRoot` / `libraryRoot` / `artifactRoot` | 初始化后的绝对路径。 |
| `Changed` | 一次 commit 后的 `AssetChangeSet`。 |
| `AssetReloaded` | loaded canonical asset 已原位更新。 |

Observer 按订阅顺序在 owner thread 调用。某个 observer 抛异常会被隔离，不能回滚已经提交的 transaction，也不会阻止后续 observer。

## 加载与保存

| API | 行为 |
| --- | --- |
| `Load<T>(path/id)` | 返回 canonical instance；缺失或类型不兼容时抛异常。 |
| `TryLoad<T>(path/id,out asset)` | 安全失败。 |
| `LoadAsync<T>(path/id,token)` | 保持异步 API 形状，但 canonical commit 仍受 owner-thread 约束。 |
| `Import(path)` | 显式导入单一受支持 source。 |
| `Save(asset)` | 导出到现有 source path。 |
| `Save(path,asset)` | 为新资产建立初始 source identity。 |
| `Rescan()` | 对账全部 source/meta/catalog/artifact。 |

初始化会自动 `Rescan`，无需为已有文件逐个调用 `Import`。

## Catalog 与 artifact 查询

| API | 说明 |
| --- | --- |
| `TryGetInfo(path/id,out info)` | 读取 immutable `AssetInfo`。 |
| `TryGetArtifact(id,outputName,out info)` | 定位 named output。 |
| `TryGetPersistentId(path,out id)` | 不加载 runtime object 查询 source identity。 |
| `TryGetAssetType(path,out type)` | 从 Catalog/Importer 解析类型。 |
| `BuildAsync(definition,inputs,token)` | 调用自动发现的 aggregate Build Processor。 |
| `GetDependencies(asset,recursive)` | runtime dependency graph。 |
| `GetReferenceInfo(asset)` | engine-known reference diagnostics。 |
| `GetLoadedPaths()` | 当前 canonical cache path snapshot。 |
| `UnloadUnusedAssets()` | 协作式释放无外部 managed root 的实例。 |

```csharp
if (AssetManager.TryGetInfo("Scripts/Player.cs", out AssetInfo? script) &&
    script.status == AssetImportStatus.Imported &&
    AssetManager.TryGetArtifact(script.persistentId, "source", out AssetArtifactInfo? source))
{
    Console.WriteLine(source.absolutePath);
}
```

## Source 文件树

`GetFileSystemEntries`、`GetFileSystemChildren` 和 `TryGetFileSystemEntry` 返回统一 Source Policy 过滤后的视图。`.imeta`、artifact、IDE cache 和默认系统噪声不出现；Unsupported source 仍出现。

FileBrowser List 使用 `AssetFileEntry.nameWithoutExtension` 显示名字，Grid 保持完整 `name`。所有实际命令始终使用完整 `relativePath`。

## 外部 rename/delete/recovery

### source-only rename

当操作系统只移动 source、没有移动 `.imeta`：

1. native rename old/new path 优先关联；
2. Loader 检查目标 meta identity 冲突；
3. `.imeta` 无 overwrite 地移动到新路径；
4. Catalog path 与 loaded canonical `sourcePath` 原位更新；
5. CAS artifact 不移动，内容不变时 key 不变；
6. 发布带 ID、oldPath、newPath 的 `Moved`。

平台若只报告 delete+create，full reconcile 会按缺失 record 和 fingerprint 做唯一匹配；存在歧义时记录 `Conflict`，不会猜测。

### delete/recovery

- 只删除 source、保留 `.imeta`：状态 `Missing`，ID 与 last-successful artifact 保留；同路径恢复后原位复活 canonical instance。
- source 和 `.imeta` 都删除：保留最小 tombstone 供旧引用显示 missing，artifact 变为 GC 候选。
- duplicate source + meta：已知原路径保留 ID，新副本获得新 ID，避免两个路径争用同一身份。

## CAS 回收

AssetManager 启动、`WaitForIdle` 和低频 idle update 会回收不可达 bundle。以下 key 保留：current、last-successful，以及当前 transaction 所需内容。超出 size limit 时优先删除最旧不可达 bundle；正常情况下还尊重 grace period。

CAS 目录只占磁盘，不会因存在就常驻运行内存。Artifact key 与 Assembly runtime generation 都不会写入 Scene/Prefab schema。

## 关闭顺序

```csharp
AssetManager.Shutdown();
SerializationManager.Shutdown();
TypeCacheManager.Shutdown();
AssemblyManager.Shutdown();
IdentityManager.Shutdown();
```

完整宿主直接调用 `Shell.Shutdown()`。
