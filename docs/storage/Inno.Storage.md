# Inno.Storage

[Storage 索引](README.md) · [Runtime](Inno.Storage.Runtime.md) · [Wiki 首页](../README.md)

`Inno.Storage` 公开 `IApplicationStorage`，只接受规范化 `StorageKey`，不向脚本暴露绝对路径。全部操作支持 cancellation；写入语义是完整值原子替换。

## 公开 API

| API | 语义 |
| --- | --- |
| `StorageKey` | 不允许 rooted、空段、`.`、`..` 或 drive-qualified 的 slash key。 |
| `IApplicationStorage` | `ExistsAsync`、`ReadAsync`、`WriteAsync`、`DeleteAsync`、`ListAsync`。 |
| `StorageExecutionContext` | `AsyncLocal` 隔离且严格 LIFO。 |
| `Storage` | 脚本友好的无状态异步 façade。 |

```csharp
using InnoEngine.Storage;

StorageKey key = new("saves/slot-1.bin");
await Storage.WriteAsync(key, bytes);
byte[]? restored = await Storage.ReadAsync(key);
```

无活动 scope 时 façade 抛出明确异常。应用存档格式由项目或 Plugin 定义，不进入本体 Storage。

[下一页：Inno.Storage.Runtime](Inno.Storage.Runtime.md)
