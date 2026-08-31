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
- `Move`
- `Delete`
- `CreateDirectory`
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
脚本 generation 切换后的第一次 `Update`/`Rescan` 会移除 `Changed` 与 `AssetReloaded` 中声明类型或 target 类型已经退休的 collectible observer，防止静态事件反向保留旧 ALC。Host observer 和当前活动 generation 的 observer 不受影响。

## 加载与保存

| API | 行为 |
| --- | --- |
| `Load<T>(path/id)` | 返回 canonical instance；缺失或类型不兼容时抛异常。 |
| `TryLoad<T>(path/id,out asset)` | 安全失败。 |
| `LoadAsync<T>(path/id,token)` | 保持异步 API 形状，但 canonical commit 仍受 owner-thread 约束。 |
| `Import(path)` | 显式导入单一受支持 source。 |
| `Save(asset)` | 导出到现有 source path。 |
| `Save(path,asset)` | 为新资产建立初始 source identity。 |
| `Move(oldPath,newPath)` | 事务式移动 source 与 `.imeta`，保留 persistent ID、canonical instance 和 artifact。 |
| `Delete(path)` | 事务式删除 file/directory source 与 sidecar；释放路径并保留 ID tombstone。 |
| `CreateDirectory(path)` | 创建带稳定 `.imeta` 的 source folder；folder 不生成 artifact。 |
| `Rescan()` | 对账全部 source/meta/catalog/artifact。 |

初始化会自动 `Rescan`，无需为已有文件逐个调用 `Import`。

`Rescan` 同时是 TypeCache generation 的资源收敛安全点。若已加载 canonical asset 的运行时类型已退休，Loader 会从内部 record、identity 和 dependency retention 中释放它；仍存活的 host asset 会用当前 generation 重新恢复其序列化引用。调用方自己仍强持有旧 canonical instance 时，旧 collectible ALC 会按普通 CLR 引用规则延迟卸载，这不影响 Loader 返回当前 generation 的新实例。

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

平台若只报告 delete+create，Loader 会在提交删除前按缺失 record 和 fingerprint 做唯一匹配。存在歧义时不会猜测：新 source 获得新 ID，旧 ID 各自进入 tombstone，并在新记录上保留明确 diagnostic。

Watcher 增量刷新失败时会立即尝试 full rescan。第一次失败和 recovery 失败都是需要保留的 Log 事件；只有两条路径都失败、Source Database 仍处于不一致状态时才发布 `Asset Source Database` Diagnostic。后续任一成功 refresh/rescan 会自动清除该状态。

FileBrowser 的 New Folder、Rename 和 Delete 全部调用 AssetManager 的事务 API。`Move` 会暂停 watcher、提交单一 `Moved` change，然后恢复 watcher；目标 source 或 `.imeta` 已存在时明确失败，不进行覆盖。

### delete/recovery

- watcher quiet window 内出现 delete+create：折叠为 `Modified`，保留 ID，不产生短暂 Missing。
- quiet window 后确认 source 已删除：自动删除孤立 `.imeta`，旧路径立即释放；Catalog 仅按 ID 保留最小 tombstone，current/last-successful artifact 引用立即清空。
- 同路径稍后重新创建但没有原 `.imeta`：视为新资产并生成新 ID，不会误继承已删除资产的身份。
- source 与原 `.imeta` 一起恢复：在 ID 无冲突时可以重新采用原 identity；若旧 canonical 仍存在则原位恢复。
- source 和 `.imeta` 同时删除：与确认后的 source-only delete 结果一致。
- duplicate source + meta：已知原路径保留 ID，新副本获得新 ID，避免两个路径争用同一身份。

## CAS 回收

AssetManager 启动、`WaitForIdle` 和低频 idle update 会回收不可达 bundle。确认删除会立即解除 Catalog 中的 current/last-successful 引用；物理 bundle 仍由 reachability、共享引用、size limit 和 grace period 决定何时删除。超出 size limit 时优先删除最旧不可达 bundle；正常情况下尊重 grace period。

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
