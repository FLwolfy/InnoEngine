# Inno.Assets.File

[上一页：Assets.Core](Inno.Assets.Core.md) · [Assets 索引](README.md) · [下一页：Assets.Loader](Inno.Assets.Loader.md)

`Inno.Assets.File` 只负责物理 Source Tree：过滤、只读索引和 watcher 原始事件队列。Watcher 线程不调用 Loader、AssetManager 或 Editor observer。

## AssetSourcePolicy

`AssetSourcePolicy` 集中管理默认 ignore 项，FileBrowser 不再维护第二套规则。默认排除 IDE/VCS/build 目录、系统噪声文件、swap/backup/temporary 文件、`.imeta`、旧 `.abin`，以及 plugin companion `.pdb` 与 `.deps.json`。Companion 仍由 DLL Importer 作为依赖读取。

可通过构造函数追加 filename、directory、prefix、suffix 规则。`defaultPolicy` 是默认实例，`IsIgnored(path,isDirectory)` 可用于工具侧一致过滤。

## AssetFileSystem

```csharp
using var files = new AssetFileSystem(
    assetRoot,
    autoStart: true,
    flushDelayMs: 80,
    sourcePolicy: AssetSourcePolicy.defaultPolicy);

IReadOnlyList<AssetChangedEvent> changes = files.PollChanges();
```

| API | 行为 |
| --- | --- |
| `Start` / `Stop` | 控制 watcher。 |
| `Refresh` | 在调用线程递归重建 source index。 |
| `PollChanges` | 在调用线程取出 quiet batch，并刷新 index。 |
| `WaitForIdle` | 等待 quiet window 后 poll；主要用于测试和工具。 |
| `Exists` / `TryGetEntry` | 按完整 `AssetPath` 查询。 |
| `GetEntries` / `GetChildren` | 返回稳定只读快照。 |

不存在 `ChangedBatch` 回调。Owner 必须显式 poll，因此公共 observer 不会意外运行在 `FileSystemWatcher` ThreadPool callback 上。

`FileSystemWatcher.Error` 或 buffer overflow 只设置 `requiresFullRescan`；异常不会逃逸 watcher thread。AssetManager 在下一次 `Update()` 做完整对账。

## AssetFileEntry

| 属性 | 示例 `Scripts/Tool.editor.cs` |
| --- | --- |
| `assetPath` | `project::Scripts/Tool.editor.cs`（项目路径显示时省略 `project::`） |
| `parentAssetPath` | `project::Scripts` |
| `name` | `Tool.editor.cs` |
| `nameWithoutExtension` | `Tool.editor` |
| `extension` | `.cs` |
| `isDirectory` | `false` |

`nameWithoutExtension` 只去掉最后一层扩展名，正是 FileBrowser List 的显示语义。选择、拖拽、双击和保存始终使用完整 `assetPath`。
Source root entry 没有 local segment，因此 `name`/`nameWithoutExtension` 使用语义标签：Project root 为 `Assets`，Plugin root 为稳定 Plugin ID。这让 Plugins overview 的 Tree、List、Grid 与 Inspector 共享同一条目身份，不需要伪造第二种文件节点。

## Rename/delete 规范化

`AssetChangedEvent` 保存 `relativePath`、`changeType` 和可选 `oldRelativePath`。Normalizer 会合并同一 quiet window 内的重复 create/change/delete，并优先保留原生 rename 的 old/new 关系。

目录 rename 后 index 会按真实磁盘 subtree 重建，不依赖操作系统一定为每个 child 发送事件。若原生平台只报告 delete+create，Loader 在 full reconcile 中使用缺失记录与 source fingerprint 做无歧义关联。

## 路径安全

所有目录查询接受 `AssetPath`，其中 Source ID 决定 Mount、`localPath` 内部统一 `/`。Rooted path、未激活 Source ID 和 traversal 会抛出 `ArgumentException`。每个 Mount 根目录 entry 的 `assetPath.localPath` 是空字符串。`AssetChangedEvent` 仍是可写 Project Mount 的物理 watcher 协议，所以它有意保存 mount-local 字符串；它不属于公开资产寻址协议。
