# Inno.Assets.File

[上一页：Assets.Core](Inno.Assets.Core.md) · [Assets 索引](README.md) · [下一页：Assets.Loader](Inno.Assets.Loader.md)

`Inno.Assets.File` 提供源目录的内存索引和批量文件变更事件。公开入口是 `AssetFileSystem`；底层 watcher 与 batch normalizer 是 internal 实现。

## AssetFileSystem

```csharp
using AssetFileSystem files = new(
    assetRoot,
    autoStart: true,
    flushDelayMs: 80);
```

| 成员 | 说明 |
| --- | --- |
| `assetRoot` | 构造时规范化的绝对根目录。 |
| `isWatching` | FileSystemWatcher 是否活动。 |
| `ChangedBatch` | batch 已规范化且 index 已 Refresh 后触发。 |
| `Start()` / `Stop()` | 开始/停止 watcher。Start after Dispose 会失败。 |
| `Refresh()` | 递归重建当前源树索引。 |
| `Exists(relativePath)` | entry 是否存在。 |
| `TryGetEntry(relativePath, out entry)` | 尝试按路径解析。 |
| `GetEntries(includeDirectories=true)` | 稳定排序快照。 |
| `GetChildren(parentRelativePath)` | 直接 children；目录优先。 |
| `WaitForIdle()` | 等待当前 watcher 事件完全 flush；未监视时立即返回。 |
| `Dispose()` | 停 watcher 并释放资源。 |

路径必须为 source-relative，分隔符规范为 `/`；包含 `..` 越界或 rooted path 会被拒绝。根目录 entry 的 relative path 是空字符串。

## AssetFileEntry

Loader 对外返回的 entry 可读取：

- `relativePath`
- `parentRelativePath`
- `isDirectory`
- `extension`：文件为 lower-case 扩展名（含 `.`），目录为空。

这些属性只有 internal setter，调用者应把 entry 当作只读索引节点。

## AssetChangedEvent

readonly struct 构造函数：

```csharp
new AssetChangedEvent(relativePath, changeType, oldRelativePath: "");
```

属性 `relativePath`、`changeType`（`System.IO.WatcherChangeTypes`）、`oldRelativePath`。Rename 才有旧路径；一个归一化 batch 的 changeType 可能组合 Created/Changed/Renamed flags。

```csharp
files.ChangedBatch += changes =>
{
    foreach (AssetChangedEvent change in changes)
    {
        if (change.changeType.HasFlag(WatcherChangeTypes.Renamed))
            Console.WriteLine($"{change.oldRelativePath} -> {change.relativePath}");
    }
};
```

## Batch 语义

- 短时间内同一路径的 create/change/delete 会合并。
- Rename 优先保留新旧路径关系。
- `.imeta` / `.abin` 生成变更由内部 watcher 过滤，避免资产系统响应自己的输出。
- `WaitForIdle` 在一个 debounce window 内没有新 generation 才返回；内部还有安全超时。

通常应用不直接创建该类型，而通过 [AssetManager 文件树 API](Inno.Assets.md#文件树查询) 访问并自动过滤生成项。
