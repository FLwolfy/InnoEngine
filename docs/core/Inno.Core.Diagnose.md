# Inno.Core.Diagnose

[上一页：Input](Inno.Core.Input.md) · [Core 索引](README.md) · [下一页：Logging](Inno.Core.Logging.md)

`Inno.Core.Diagnose` 管理 Compiler、Importer、Validator、Shader Processor 和 Build Pipeline 等生产者的“当前问题状态”。它独立于追加式 Logging：同一拥有者再次设置诊断会原子替换旧结果，成功后清除结果，后注册的工具仍能立即获得所有当前问题。

项目不执行具体编译、导入或验证，也不保存历史日志。业务系统只构造通用 `Diagnostic` 并通过静态 `Diagnostics.Set/Clear` 更新状态；Editor Console、命令行工具和构建报告通过 sink 消费不可变 report。

## 核心语义

```text
Producer completes one validation pass
→ Build the complete current Diagnostic collection
→ Diagnostics.Set(group, diagnostics)
→ Resolve caller type + explicit group
→ Atomically replace the previous report
→ Replay the new current state to every sink
```

- `Diagnostic` 是尚未发布的一条不可变问题数据。
- `Diagnostics` 是业务代码使用的静态状态入口。
- `Set` 始终替换完整集合，不逐条追加。
- `Set` 空集合等价于 `Clear`。
- `Clear` 只清除调用类型拥有的指定 group，不会清除全局状态。
- 动态目标 overload 使用 `Guid` 将不同 Asset、Shader、Scene 或其他对象隔离。
- 后注册的 sink 会立即收到所有当前 report。
- 一个 sink 抛出异常不会影响生产者或其他 sink。

## 创建诊断

```csharp
using Inno.Core.Diagnose;

Diagnostic error = Diagnostic.Error(
    "CS1002",
    "Expected ';'.",
    new DiagnosticLocation("Assets/Test.cs", line: 10, column: 24));

Diagnostic warning = Diagnostic.Warning(
    "INNO1001",
    "StableTypeId is implicit.");
```

`Diagnostic.Info`、`Diagnostic.Warning` 和 `Diagnostic.Error` 只创建数据，不修改全局状态。

## 固定职责

同一调用类型中的 group 名称用于标识一项持续职责：

```csharp
internal sealed class ScriptManager
{
    private const string C_COMPILATION_DIAGNOSTICS = "Compilation";

    internal void PublishCompilation(IReadOnlyList<Diagnostic> diagnostics)
    {
        Diagnostics.Set(C_COMPILATION_DIAGNOSTICS, diagnostics);
    }

    internal void ClearCompilation()
    {
        Diagnostics.Clear(C_COMPILATION_DIAGNOSTICS);
    }
}
```

隐藏 source ID 由调用 Assembly、逻辑调用类型和 group 组成。不同类型可以安全复用 `Compilation`、`Import` 等局部名称。方法重命名不会改变身份；发布与清除只要发生在相同调用类型并使用相同 group 即可。

## 动态目标

同一个职责处理多个对象时，使用稳定 target ID：

```csharp
Diagnostics.Set(
    asset.identity.persistentId,
    "Import",
    importDiagnostics,
    displayName: asset.assetPath.ToString());

Diagnostics.Clear(
    asset.identity.persistentId,
    "Import");
```

隐藏 source ID 额外包含 target ID，因此不同 Asset 的报告互不覆盖。`displayName` 只影响工具展示，不参与身份比较；Asset 重命名后可以用相同 persistent ID 和新路径替换原报告。

## Public API

### Diagnostic

| API | 说明 |
| --- | --- |
| `Info(code, message, location?)` | 创建 informational 诊断值。 |
| `Warning(code, message, location?)` | 创建 warning 诊断值。 |
| `Error(code, message, location?)` | 创建 error 诊断值。 |
| `severity` | `Info`、`Warning` 或 `Error`。 |
| `code` | Producer 定义的稳定编号。 |
| `message` | 面向用户的问题描述。 |
| `location` | 可选的 source path、one-based line 和 column。 |

### Diagnostics

| API | 说明 |
| --- | --- |
| `Set(group, diagnostic)` | 用单条诊断替换调用类型的指定 group。 |
| `Set(group, diagnostics)` | 用完整集合替换调用类型的指定 group；空集合会清除。 |
| `Set(targetId, group, diagnostic, displayName?)` | 设置一个动态目标的单条当前诊断。 |
| `Set(targetId, group, diagnostics, displayName?)` | 设置一个动态目标的完整当前集合。 |
| `Clear(group)` | 清除调用类型的指定 group。 |
| `Clear(targetId, group)` | 清除一个动态目标的指定 group。 |

### DiagnosticManager

| API | 说明 |
| --- | --- |
| `RegisterSink(sink)` | 注册消费者，并立即 replay 所有当前报告。 |
| `UnregisterSink(sink)` | 停止向消费者发送后续状态变化。 |

生产者不直接调用 Manager。Manager 的状态写入入口属于程序集内部实现，防止业务代码绕过 caller/group 身份规则。

### DiagnosticSource 与 DiagnosticReport

`DiagnosticSource` 是 Manager 生成的只读身份元数据，公开 `id` 和 `displayName` 供工具建立索引与展示。业务代码不能直接构造 source。

`DiagnosticReport` 包含 source、不可变的完整 diagnostics 集合和发布时间，由 Manager 创建并发送给 sink。

### IDiagnosticSink

```csharp
public interface IDiagnosticSink
{
    void Replace(DiagnosticReport report);
    void Clear(DiagnosticSource source);
}
```

Sink 必须把 `Replace` 看作完整集合替换，而不是增量追加。

## 与 Logging 的边界

```text
Log
    = 已经发生过的事件、调试时间线、异常细节

Diagnostic
    = 当前仍然存在、具有明确 owner 和 clear 时机的问题
```

意外异常导致系统持续降级时，可以将完整异常写入 Log，同时发布简洁的当前 Diagnostic。系统恢复后只清除 Diagnostic，不删除历史 Log。

当前内建生产者采用同一规则：

| 子系统 | Diagnostic | Log |
| --- | --- | --- |
| Asset Loader | 当前 Import、Build、Catalog、missing reference 与 identity conflict 状态 | 实际发生的 Import/Build/Catalog 异常及完整堆栈 |
| AssetManager | 增量刷新与 recovery rescan 同时失败后的 Source Database 状态 | 每次 refresh/rescan 失败事件 |
| Scripting | 当前 compiler 与 reload 结果 | reload 事务抛出的完整异常 |
| Scene Workspace | 当前无法恢复的 scene setup、持续失败的 document synchronization | 首次进入失败状态的异常，以及被跳过的 missing scene 事件 |
| Editor Workspace | 当前 capture、restore、save 失败 | 状态首次变化时的完整异常 |
| Editor Extensions | 当前无法 Attach 的 Panel 集合 | Attach/Detach 的实际失败事件 |
| Editor Application | 当前无法持久化 `editor.ini` 的状态 | 首次保存失败的完整异常 |

Action、Menu、Drag/Drop、Undo/Redo、Rename/Delete/Open 和 Inspector 单次绘制失败继续只使用 Log；它们是一次调用的结果，不是拥有明确恢复事务的长期状态。

## 注意事项

- group 必须是当前调用类型内稳定、语义明确的职责名称。
- 多个问题必须在一次 `Set` 中提交，避免后一次调用覆盖前一次结果。
- 动态对象必须使用稳定 `Guid`，不能使用 runtime hash code 或可变化路径作为身份。
- `DiagnosticLocation` 表示被诊断的源码位置，不参与 report owner 身份。
- Diagnostics 不经过 EventHub；它需要保存当前快照并 replay 给晚订阅者。
- Console UI、过滤、折叠、复制、源码跳转和 Quick Fix 属于 Editor 层。

## 相邻模块

- [Inno.Core.Logging](Inno.Core.Logging.md)：追加式历史日志。
- [Inno.Editor.Panel.Logging](../editor/Inno.Editor.Panel.Logging.md)：合并展示 Log 和当前 Diagnostics。
- [Inno.Editor.Scripting](../editor/Inno.Editor.Scripting.md)：脚本编译和 reload 诊断生产者。
