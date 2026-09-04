# Inno.Core.Logging

[上一页：Diagnose](Inno.Core.Diagnostics.md) · [Core 索引](README.md) · [下一页：Mathematics](Inno.Core.Mathematics.md)

Logging 是基础、追加式日志系统。`LogRouter` 显式拥有一个 Host 的异步队列、过滤策略和 Sink；`Log` 只是解析当前执行上下文的脚本便利门面。Compiler、Importer 和 Validator 的可替换当前问题属于独立的 [Inno.Core.Diagnostics](Inno.Core.Diagnostics.md)。

## 初始化

```csharp
using var router = new LogRouter();
router.RegisterSink(new ConsoleLogSink());
router.RegisterSink(new FileLogSink(Path.Combine(projectRoot, "Logs")));
router.SetMinimumLevel(LogLevel.Info);

using (router.EnterScope())
    Log.Info("Loaded {0} assets", count);

router.Flush();
```

`Dispose` 会停止 worker、排空队列，并 Dispose 所有实现 `IDisposable` 的已注册 sink。`EngineHost` 正常情况下统一拥有并释放 Router。

## Log

每个等级都有 `object?` 与 `(string message, params object[]? args)` overload：

- `Debug`：仅在 `DEBUG` 条件编译存在调用。
- `Info`
- `Warn`
- `Error`
- `Fatal`

格式化采用 `string.Format`。调用方类型名作为 `category`；调用程序集的当前分类解析为 `domain` 与 `scope`。分类使用弱缓存，不固定热重载 ALC；日志 entry 只复制 enum、字符串、时间与行号，不保存调用方 `Type`/`Assembly`。

`Log` 已作为 Runtime Scripting API 导出到逻辑 namespace `InnoEngine.Logging`。Project 脚本不引用真实的 `Inno.Core.Logging` namespace：

```csharp
using InnoEngine.Logging;

Log.Info("Player spawned at {0}", transform.localPosition);
Log.Warn("Health is low: {0}", health);
```

只导出便捷门面 `Log`；`LogRouter`、sink 和日志分发配置仍由 Host 管理，不向游戏脚本开放。

## LogRouter

| API | 说明 |
| --- | --- |
| `RegisterSink(ILogSink)` | 添加接收器；允许多个。 |
| `UnregisterSink(ILogSink)` | 移除接收器，但不自动 Dispose。 |
| `SetMinimumLevel(LogLevel)` | 设置最低分发等级。 |
| `CreateLogger<TOwner>()` | 为实例服务创建显式 category logger。 |
| `Dispatch(LogEntry)` | 直接投递构造好的日志项。 |
| `Flush()` | 等待调用前已入队的日志全部送达 sink；只用于低频生命周期边界。 |
| `EnterScope()` | 将 Router 绑定到当前异步执行上下文，供脚本 `Log` 门面解析。 |
| `Dispose()` | 停 worker、flush、清空并释放 sink。 |

单个 sink 的 `Receive` 异常会被隔离并将该 sink 从 Router 原子隔离；`sinkFailed` 会报告失败，而健康 sink 继续接收日志。`Flush()` 通过异步队列中的 barrier 保证先前日志已送达，不会把常规日志调用改成同步分发；从 logging worker 自身调用会抛出 `InvalidOperationException`，避免等待自身造成死锁。

## LogEntry 与 LogLevel

`LogLevel` 按严重程度递增：`Debug`、`Info`、`Warn`、`Error`、`Fatal`。

`LogEntry` readonly struct 构造参数/字段：

| 字段 | 类型 | 说明 |
| --- | --- | --- |
| `level` | `LogLevel` | 严重程度。 |
| `domain` | `AssemblyDomain` | InnoInternal/InnoScripting/InnoPlugin 所有权。 |
| `scope` | `AssemblyScope` | Runtime/Editor 依赖范围。 |
| `category` | `string` | 通常是调用类型名。 |
| `message` | `string` | 已渲染文本。 |
| `time` | `DateTime` | 构造时本地时间。 |
| `file` | `string` | 调试符号可用时的调用文件。 |
| `line` | `int` | 调用行号。 |

## Sink API

`ILogSink.Receive(LogEntry)` 是唯一契约。

### ConsoleLogSink

`Receive` 根据等级设置 console color，并输出时间、domain/scope、category 与消息。

### FileLogSink

```csharp
using FileLogSink sink = new(
    logDirectory,
    maxFileSizeBytes: 10 * 1024 * 1024,
    maxFiles: 10);
```

- `C_LOG_FILE_PREFIX == "log_"`。
- 生成文件使用 `log_<timestamp>.log` 命名并存放在调用方提供的日志目录中。
- `Receive` 入自己的异步队列。
- 达到 `maxFileSizeBytes` 后轮换文件。
- 只保留最多 `maxFiles` 个匹配日志文件。
- `Dispose()` 停止 worker 并 flush。

## 注意事项

- File sink 又有独立 worker，因此必须 Dispose 才能保证最后的日志落盘。
- `Debug` 不是单纯运行时过滤；非 DEBUG 构建中调用会被编译器移除。
- 日志格式参数错误会在生产日志的一侧抛出；不要把不可信文本直接当 composite format。
