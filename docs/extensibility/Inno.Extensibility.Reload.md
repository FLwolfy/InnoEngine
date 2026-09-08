# Inno.Extensibility.Reload

[Extensibility 索引](README.md) · [Wiki 首页](../README.md) · [Identity、可恢复引用与热重载标准](../architecture/IDENTITY_REFERENCE_RELOAD_STANDARD.md)

当前新增 `GenerationCoordinator.TryAcquireChange(operation, out reservation)`：后台读租约仍存活时返回 false，
供自动 Catalog 刷新延后，不绕过读保护。持有发布 reservation 时不接受新的 Build/Export 读者。
`Fault(exception)` 是 owner 发现不可恢复清理错误时关闭整个 Host admission 的入口；没有 Reset 或继续运行分支。
`EnsureRetirementSafe()` 是依赖退出的统一前置校验：普通终结错误仍允许继续清理；曾报告未退休工作后，
即使更早已有另一条普通错误，也始终抛出未退休信号。Registry、TypeCatalog、ModuleHost、RuntimeSession、EngineHost 共用此判断。

## 职责与边界

`Inno.Extensibility.Reload` 定义所有 collectible generation 共用的 GC unload barrier。它不加载程序集、
不扫描类型，也不决定哪个 candidate 应当激活；这些职责仍分别属于 Module、Scripting 与 Plugin owner。

- `IAssemblyUnloadProbe` 只通过弱状态观察一个退休 generation。
- `AssemblyUnloadBarrier` 执行 Full GC、等待 finalizer、再次 Full GC，并在所有 probe 完成前保持
  `AwaitingCollection`。
- `AssemblyUnloadException` 是终止性 retention failure，记录仍存活的 generation、等待时长与 GC 次数。
- `AssemblyUnloadBarrierState.Faulted` 不会自动回到 Completed；owner 必须阻止 Play、Build、Export 与下一次 reload。

## 标准流程

`GenerationCoordinator` 是 ModuleHost 持有的统一 admission owner，状态为 Ready/Transitioning/AwaitingCollection/Faulted。`Execute(operation, publication, changes)` 使用 `IGenerationPublication<TProbe>` 与 `IGenerationChange` 完成 prepare、activate、apply、complete；失败时逆序撤销结构、恢复旧 publication 和旧状态。不可逆清理/回滚失败进入 Faulted。

`Configure` 复制 GC 策略；`EnsureReady` 检查新的变更/Play admission；`AcquireRead` 允许并行 Build/Export reader，并阻止期间开始 generation transaction；`TrackRetirement` 不丢弃已 pending monitor；`Advance` / `Wait` 驱动退休。GC/finalizer 等待不持有 admission lock。ScriptReloadHost 与 EditorReloadCoordinator 复用该 owner，不各维护一个 barrier。

`AcquireOperation(operation)` 是**同步领域操作**的最小保护边界：从捕获当前 owner 到修改、观察者通知和补偿
结束均持有 scope，期间自动 Catalog 刷新延后。发布事务只能在自己的控制线程借用该 scope，其他线程被拒绝；
借用 scope 的 Dispose 不会提前结束 publication。已经发布的数据在 AwaitingCollection 时仍可访问，但这不授权
Play、Build、Export 或新 reload；这些操作继续使用 `EnsureReady` / `AcquireRead`。Faulted 时所有访问均被拒绝。
该 API 不自动刷新 Catalog；需要接纳最新快照的 owner 应在捕获领域对象前先对账，再取得 scope。
`TypeCatalog.AcquireOperation` 是相同 gate 的转交入口，不是第二套生命周期协议。

Core `RetirementPendingException` 是与普通清理错误不同的所有权信号。Execute 在 prepare、commit cleanup
或 rollback 中收到它时，保留 publication 与领域 change 数组并进入 Faulted，原样抛出；不继续卸载下层，
不恢复可能仍被在途工作使用的旧结构，也不清空事务后回到 Ready。可重试排空应由领域 owner 在返回前
通过 `RetirementBarrier` 完成；到达跨 generation 事务边界仍未排空就不能再继续该进程的代际操作。

上述规则也适用于包装异常。GenerationCoordinator 不再维护自己的异常递归函数，使用 Core
`RetirementPendingException.Find`；Module、TypeRegistry、Reference Recovery、Host 和各领域复用同一分类。
事务 catch 保留原始异常与未完成 transaction，不清空 participant 数组、不落入普通补偿路径继续销毁依赖。

下面的低层 barrier 示例适合实现 probe/测试；产品流程应复用 host.generations，而不是创建并行 gate。

```csharp
var barrier = new AssemblyUnloadBarrier(
    probes,
    new AssemblyUnloadBarrierOptions
    {
        collectionInterval = TimeSpan.FromMilliseconds(250),
        retentionTimeout = TimeSpan.FromSeconds(30)
    });

while (!barrier.Advance())
{
    // Return to the Editor frame loop without publishing reload success.
}
```

Shutdown 可以调用 `Wait()`；交互式 Editor 应逐帧调用 `Advance()`。达到阈值时异常必须成为 reload
subsystem 的持久 Fault，而不是清空 monitor 后继续运行。

## 依赖与相邻项目

本项目属于 Foundation，依赖 Core Execution 的退休信号，不依赖 Assets、Scene、Runtime 或 Editor。`Inno.Extensibility.Modules` 提供具体
`AssemblyUnloadMonitor` probe；`Inno.Scripting.Reload` 和 Plugin composition 使用 barrier 控制可见完成语义。
