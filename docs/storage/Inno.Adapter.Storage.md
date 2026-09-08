# Inno.Adapter.Storage

[Storage 索引](README.md) · [中立 Storage](Inno.Storage.md) · [FileSystem implementation](Inno.Adapter.Storage.FileSystem.md)

该项目定义应用存储 Adapter family，不包含文件系统路径实现。

## 公开 API

- `StorageBackend`：Composition 启动时使用的存储后端选择。
- `IStorageBackendFactory.CreateStorage`：从 backend 与宿主批准的 root 创建 caller-owned `IApplicationStorage`。

Factory 负责 implementation 选择；`IApplicationStorage` 继续负责 key sandbox、异步读取与原子提交。游戏脚本永远不接触绝对路径或 `FileSystemApplicationStorage`。

```csharp
using IApplicationStorage storage = catalog.storage.CreateStorage(selection.storage, persistentRoot);
```

未知 backend、无效 root 或初始化失败必须在 Composition/Session 启动边界明确报告。
