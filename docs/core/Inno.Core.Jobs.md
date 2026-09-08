# Inno.Core.Jobs

## 有界调度

`JobSchedulerOptions.maxFrameJobs`、`mainThreadCapacity`、`mainThreadDrainBudget` 同时适用于单线程和 worker pool；0 分别采用 65536、65536、4096。负值拒绝。

Frame job 超限或主线程回调队列满时明确抛 InvalidOperationException，调用者不得把异常当作已接受。Drain 最多执行入口时已有的 budget 数量，递归 enqueue 留到后续 drain，不无限吞掉一帧。

Dispose 会先完成活动 frame 的 job，再关闭队列并清除剩余通知/任务记录中的托管引用。它不能中断任意不返回的用户回调。

[Core 索引](README.md) · [Wiki 首页](../README.md) · [Runtime](../runtime/Inno.Runtime.md)

## 公开 API

- `JobScheduler`：session-owned scheduler，支持单线程与工作窃取执行。
- `JobSchedulerOptions`, `JobExecutionMode`：线程和执行策略。
- `JobSchedulerStatistics` / `JobScheduler.statistics`：frameJobs、peakFrameJobs、rejectedJobs、mainThreadPending、mainThreadPeak、mainThreadRejected、mainThreadCanceled 的中立计数快照。
- `JobHandle`：带 generation 的依赖/完成句柄。

一个 scheduler 只能由其 owner 按 BeginFrame/EndFrame contract 驱动。单线程模式拒绝跨线程 mutation；工作窃取模式允许并发 schedule，但 main-thread queue 只在 owner thread drain。stale handle、重复 frame 和 job exception 明确失败。

## 所有权、接纳与失败

Schedule 先验证全部依赖，再分配 record；ParallelFor 在同一个 admission 临界区预留所有分块及 combine record，超限不会留下部分 job。失败 Task/Job 不伪装成成功完成。

Main-thread queue 的 capacity 限制等待项；正在执行的一个 callback 另计，因此 pending/peak 最多是 capacity + 1。Drain 只处理入口快照，不递归耗尽新入队工作。callback 报 Pending 时保留同一个 callback 和 Core RetirementBarrier，后续安全点重试；Close 取消未开始项，但必须退休已经开始的步骤。普通失败跨 Pending 保留，最终一起报告。完成清理后清掉异常引用。

Worker pool 构造失败会停止、唤醒并 join 已启动的线程，再释放同步对象。构造完成前不接纳任务。同步 job 和 callback 必须有限返回；不支持安全抢占任意无限循环代码，也不能把 shutdown deadline 当作 Thread.Abort。
