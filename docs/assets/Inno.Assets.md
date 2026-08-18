# Inno.Assets

[Assets 索引](README.md) · [下一页：Assets.Core](Inno.Assets.Core.md) · [Wiki 首页](../README.md)

`Inno.Assets` 是应用层资产门面。它组合 File index、Loader、Serialization resolver 与 watcher，提供强类型同步/异步加载、导入、保存、引用诊断和变更事件。游戏与编辑器业务优先调用这里，而不是直接持有 `AssetLoader`。

## 初始化

AssetManager 依赖 Identity、TypeCache 和 Serialization 已初始化：

```csharp
IdentityManager.Initialize();
AssemblyManager.Initialize(assemblyOptions);
TypeCacheManager.Initialize();
SerializationManager.Initialize();

AssetManager.Initialize(AssetManagerOptions.Create(
    Path.Combine(projectRoot, "Assets"),
    Path.Combine(projectRoot, "Library", "Artifacts")));
```

`AssetManagerOptions.Create` 默认开启 watcher，debounce 为 80 ms。也可显式构造：

| 属性 | 说明 |
| --- | --- |
| `assetRoot` | 源资产根目录，必填。 |
| `artifactRoot` | 导入产物根目录，必填。 |
| `enableFileSystemWatcher` | 是否监视源文件变化并热刷新。 |
| `fileWatcherFlushDelayMs` | watcher 合并事件的毫秒窗口。 |

## 状态与事件

| 成员 | 说明 |
| --- | --- |
| `isInitialized` | 服务是否可用。 |
| `assetRoot` / `artifactRoot` | 初始化后的绝对路径；关闭后为空。 |
| `SourceFileSystemChanged` | 规范化变更已经应用到 Loader/File index 后触发。 |
| `AssetReloaded` | 已加载 canonical asset 已原位更新后触发。 |
| `Initialize(options)` / `Shutdown()` | 建立/释放全局服务。重复 Initialize 会先关闭旧服务。 |

Observer 异常被隔离，不能回滚已经提交的 Manager 状态。Shutdown 会清除事件订阅。

## 加载 API

| API | 行为 |
| --- | --- |
| `Load<TAsset>(string relativePath)` | 按源相对路径加载 canonical instance；失败/类型不兼容抛异常。 |
| `Load<TAsset>(Guid persistentId)` | 按持久 ID 加载。 |
| `TryLoad<TAsset>(path/id, out asset)` | 安全失败返回 false。 |
| `LoadAsync<TAsset>(path/id, CancellationToken)` | 异步等待共享 in-flight load，返回同一 canonical instance。 |

```csharp
TextAsset config = AssetManager.Load<TextAsset>("Config/game.json");

if (AssetManager.TryGetPersistentId("Config/game.json", out Guid id))
{
    TextAsset same = await AssetManager.LoadAsync<TextAsset>(id);
    Debug.Assert(ReferenceEquals(config, same));
}
```

调用者 cancellation 只取消当前等待，不会取消其他调用者共享的底层加载。

## 导入与保存

| API | 说明 |
| --- | --- |
| `Import(relativePath)` | 用支持该扩展名的 Importer 生成 `.imeta` 与 artifact；被处理时 true。 |
| `Save(AssetObject)` | 导出到现有 `sourcePath`；未保存对象会失败。 |
| `Save(relativePath, AssetObject)` | 给新资产指定初始路径并导出。 |
| `Rescan()` | 对账源文件、metadata、artifact、持久 catalog 与文件索引。 |

Save 依赖 Importer 覆盖 `TryExport`。成功导入/保存后 File index 会 Refresh。

## Catalog 和引用查询

| API | 说明 |
| --- | --- |
| `TryGetAssetType(path, out Type?)` | 不加载资产，仅从 metadata 解析具体类型。 |
| `TryGetPersistentId(path, out Guid)` | 不加载资产，解析持久 ID。 |
| `GetLoadedPaths()` | 已加载 canonical asset 的稳定路径快照。 |
| `GetDependencies(asset, recursive=false)` | 直接或传递 runtime asset dependencies。 |
| `GetReferenceInfo(asset)` | engine-known 引用位置诊断；不是 CLR strong-reference 计数。 |
| `UnloadUnusedAssets()` | 释放无外部 managed reference 的 canonical asset，返回数量。 |

`UnloadUnusedAssets` 会触发资产的运行时资源释放，但只要外部仍保留强引用就不会回收对应实例。

## 文件树查询

| API | 说明 |
| --- | --- |
| `GetFileSystemEntries(includeDirectories=true)` | 全部源文件/目录快照。 |
| `GetFileSystemChildren(parentRelativePath)` | 某目录直接 children，目录排在文件前。 |
| `TryGetFileSystemEntry(relativePath, out entry)` | 解析单个 entry。 |
| `WaitForIdle()` | 等待已观察到的 watcher 变更完成 debounce、应用与事件回调。 |

Manager 会从这些公开结果中过滤 `.imeta` 和 `.abin` 生成文件。

## Reload 语义

当源文件更新并成功重新导入时，Loader 优先更新已有 canonical `AssetObject` 的状态，而不是让所有引用突然指向新对象。`contentVersion` 递增，`OnRuntimePayloadChanged` 被调用，随后发出 `AssetReloaded`。

Importer 自身所在 assembly generation 变化时，即使开发者没有提升持久 `version`，本进程中的 Registry 也会将对应 importerId 的现有资产视为待重新导入；跨进程缓存仍以显式 importer `version` 为契约。

## 关闭顺序

```csharp
AssetManager.Shutdown();
SerializationManager.Shutdown();
TypeCacheManager.Shutdown();
AssemblyManager.Shutdown();
IdentityManager.Shutdown();
```

完整应用直接使用 `Shell.Shutdown()` 即可。
