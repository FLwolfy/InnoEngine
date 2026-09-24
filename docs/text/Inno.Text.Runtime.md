# Inno.Text.Runtime

[Text 索引](README.md) · [Runtime](../runtime/README.md)

`TextRuntimeFactory` 以 `inno.runtime.text` 注册 Session Subsystem（order `-800`）。`TextRuntime` 持有 `ITextBackend`、字体 artifact lease 与 execution scope；Session 结束时释放后端和 lease。它不保存 Scene 对象，也不负责 GPU 上传。UI 的 Session Subsystem 明确依赖它。

公开构造入口 `TextRuntime(ITextBackend, IAssetArtifactLookup)` 明确转交 backend 所有权；`TextRuntimeFactory(Func<RuntimeSubsystemContext, TextRuntime>)` 是 Composition 用的 factory。`EnterExecutionScope` 可为受控工具/测试显式绑定 `Text` 门面；正常运行由 `OnBeginFrame` 自动绑定。`Shape`/`Rasterize` 只在 FontAsset 可解析且 artifact 完整时工作，缺失或已销毁的 backend 明确失败。扩展后端应实现 `ITextBackend` 并通过 `Inno.Adapter.Text` 工厂进入 Session，不继承此 Runtime。

## 生命周期与扩展点

Factory 的公开 `descriptor` 声明启动顺序，`Create(RuntimeSubsystemContext)` 为每个 Session 创建独立 Runtime。`TextRuntime.Shape` 先解析字体 artifact 并缓存 face，`Rasterize` 使用同一 face；`OnStop` 逆序释放原生 face、backend 和 artifact lease。`OnBeginFrame` / `OnStop` 仅为 RuntimeSubsystem 生命周期覆写，不是脚本入口。

```csharp
using Inno.Text;

using IDisposable scope = runtime.EnterExecutionScope();
TextLayout title = Text.Shape(font, "界面", new TextStyle(24f));
```

此段供拥有 `TextRuntime` 的 Host 使用；普通脚本在活动帧内直接使用 `Text`。错误的字体引用、已停止的 Runtime、无活动 scope 或后端错误都会明确失败；热重载后不可保存旧 face handle。
