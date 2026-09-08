# Inno.Storage.Runtime

## 异步所有权

所有五种异步操作通过 LifetimeScope.RunAsync 接受并跟踪，使用调用者和 Runtime 组合取消信号。停止后拒绝新操作；有已接受操作仍运行时保留 backend 并抛 RetirementPendingException，完成后在 owner-thread 重试释放。

[Storage 索引](README.md) · [Contract](Inno.Storage.md) · [FileSystem Adapter](Inno.Adapter.Storage.FileSystem.md)

`Inno.Storage.Runtime` 把一个 `IApplicationStorage` 绑定到完整 Session 生命周期。RuntimeSubsystem 在 BeginFrame 打开 scope，EndFrame 恢复父 scope；Stop 等全部异步工作退休后释放实现了 `IDisposable` 的 adapter。

`StorageRuntimeFactory` 接受 Composition Root 的 factory callback，并为每个 Edit、Play 或 Player Session 创建独立存储实例。创建 null、重复 Subsystem ID 或依赖环会在 Session 启动时失败，不会进入半初始化状态。

[下一页：Inno.Adapter.Storage.FileSystem](Inno.Adapter.Storage.FileSystem.md)

## 接纳与公开状态

`StorageRuntime(storage, maxPendingOperations = 128, maxWriteBytes = 67108864)` 显式组合服务和预算。五种 operation 共用有界 LifetimeScope；容量不足在调用 backend 前拒绝。Write 在已接纳 operation 的同步前缀复制完整 bytes，调用者之后修改输入不会修改进行中的写入。预算不是磁盘配额，文件系统边界另由 adapter 负责。

`pendingOperations`、`rejectedOperations` 提供中立统计。公开存储能力为 ExistsAsync、ReadAsync、WriteAsync、DeleteAsync、ListAsync；`storage` 返回同一个受限服务，不能绕过接纳直接拿 backend。没有额外 protected 扩展点。
