# Inno.Adapter.Input

[Input 索引](README.md) · [中立 Input](Inno.Input.md) · [SDL3 implementation](Inno.Adapter.Input.Sdl3.md)

该项目定义平台事件到 Session Input backend 之间的 Adapter family，不包含 SDL3 引用。

## 公开 API

- `InputBackend`：Composition 启动时使用的 input implementation 选择。
- `IInputBackendFactory.CreateEventSource`：为主窗口创建 application-owned event source。
- `IInputEventSource`：接收中立 `Event`，并为每个 RuntimeSession 创建隔离的 `IInputBackend`。

Shell 拥有一个 event source，并在平台事件泵中调用 `ProcessEvent`。每个 Edit、Play 或 Player session 只拥有自己由 `CreateBackend` 返回的 backend；销毁 session 不得销毁 application event source。

```csharp
using IInputEventSource source = catalog.input.CreateEventSource(selection.input, window);
using IInputBackend backend = source.CreateBackend();
```

Action Map、rebinding 和 UI navigation 不属于此 family。
