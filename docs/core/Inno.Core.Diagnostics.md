# Inno.Core.Diagnostics

[上一页：Input](Inno.Core.Input.md) · [Core 索引](README.md) · [下一页：Logging](Inno.Core.Logging.md)

Diagnostics 是独立于 Logging 的“当前问题状态”基础设施。Compiler、Importer、Validator、Shader Processor、Build Pipeline 等生产者发布自己当前的完整诊断集合；同一来源的下一次发布会整体替换上一次结果。

它不执行编译、导入或验证，不保存历史日志，也不依赖 Editor。具体系统只负责把自己的结果转换成通用 `Diagnostic`，Editor Console、命令行工具或构建报告可以通过相同 sink 契约消费这些结果。

## 核心语义

```text
Producer completes one operation
→ Build complete Diagnostic[]
→ DiagnosticManager.Publish(source, diagnostics)
→ Atomically replace that source's previous report
→ Replay the new current state to every sink
```

- 普通日志是追加式历史事件，归 `Inno.Core.Logging` 管理。
- 诊断是某个 producer 的当前状态，归 `Inno.Core.Diagnostics` 管理。
- `Publish` 接收完整集合，不使用 ambient scope、线程上下文或逐条追加。
- 发布空集合等价于 `Clear(source)`。
- 不同 `DiagnosticSource` 互不覆盖。
- 后注册的 sink 会立即收到所有仍然有效的 report。
- 单个 sink 抛异常不会影响 producer 或其他 sink。

## 发布诊断

每个 producer 定义稳定的 source：

```csharp
private static readonly DiagnosticSource COMPILER_SOURCE = new(
    "editor.scripting.compiler",
    "Script Compiler");
```

一次操作完成后发布完整结果：

```csharp
DiagnosticManager.Publish(
    COMPILER_SOURCE,
    diagnostics.Select(result => new Diagnostic(
        result.isError
            ? DiagnosticSeverity.Error
            : DiagnosticSeverity.Warning,
        result.code,
        result.message,
        new DiagnosticLocation(
            result.sourcePath,
            result.line,
            result.column))));
```

下一次操作完全成功时：

```csharp
DiagnosticManager.Publish(COMPILER_SOURCE, []);
```

这会删除该 compiler 的旧 warning/error，但不会影响普通日志、Importer 诊断或其他 source。

## Public API

### DiagnosticSource

| 成员 | 说明 |
| --- | --- |
| `id` | 稳定、机器可读的 producer 身份，也是替换键。 |
| `displayName` | 工具窗口显示的 producer 名称。 |

Source 按 `id` 判断相等。推荐使用反向域式、模块稳定的名称，例如 `editor.scripting.compiler`、`assets.importer.texture`、`renderer.shader.compiler`。

### Diagnostic

| 成员 | 说明 |
| --- | --- |
| `severity` | `Info`、`Warning` 或 `Error`。 |
| `code` | Producer 定义的稳定编号；没有时可以为空。 |
| `message` | 面向用户的问题描述。 |
| `location` | 可选 `DiagnosticLocation`。 |

`DiagnosticLocation` 保存 source path 以及可选的 one-based line/column。它既能表示 `.cs` 文件，也能表示 Shader、Asset source 或生成文件位置。

### DiagnosticManager

| API | 说明 |
| --- | --- |
| `Publish(source, diagnostics)` | 原子替换 source 的完整当前结果；空集合会清除。 |
| `Clear(source)` | 显式移除 source 的当前结果。 |
| `RegisterSink(sink)` | 注册 sink，并同步 replay 当前结果。 |
| `UnregisterSink(sink)` | 停止向 sink 发送后续变化。 |

### IDiagnosticSink

```csharp
public interface IDiagnosticSink
{
    void Replace(DiagnosticReport report);
    void Clear(DiagnosticSource source);
}
```

`DiagnosticReport` 由 Manager 创建，包含 source、不可变 diagnostics 集合和发布时间。Producer 无需直接构造 report。

## 职责边界

Diagnostics Core 应当继续负责：

- producer 身份和诊断数据模型；
- 同 source 的原子替换与清理；
- 当前 report 的内存目录；
- sink 注册、当前状态 replay 和异常隔离。

它不应负责：

- Roslyn、Shader、Asset 或 Scene 的具体分析；
- Console UI、颜色、过滤、折叠和复制；
- 日志文件和历史留存；
- 自动修复、源码跳转或 suppression policy；
- 把 diagnostic 再写成一条普通 Log。

源码跳转、Quick Fix、按 source 过滤等功能应由 Editor presentation/interaction 层根据 `DiagnosticReport` 扩展，而不是反向增加 Core 对 Editor 的依赖。
