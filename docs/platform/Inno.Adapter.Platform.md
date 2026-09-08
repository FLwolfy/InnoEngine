# Inno.Adapter.Platform

[Platform 索引](README.md) · [中立 Platform](Inno.Platform.md) · [SDL3 implementation](Inno.Adapter.Platform.Sdl3.md)

该项目定义平台 Adapter family 的稳定边界，不包含 SDL3 引用。

## 公开 API

- `PlatformBackend`：Composition 启动时使用的后端选择；当前默认值为 `Sdl3`。
- `IPlatformBackendFactory.CreateApplication`：把中立选择解析为 caller-owned `IPlatformApplication`。

Host 只能保存枚举、factory 和 `IPlatformApplication` / `IPlatformWindow`。未知或未安装的 backend 必须明确失败；不得返回伪实现，也不得让 SDL enum、pointer 或具体 window 类型离开 implementation assembly。

```csharp
IPlatformApplication application = catalog.platform.CreateApplication(selection.platform);
using IPlatformWindow window = application.CreateWindow(windowOptions);
```

相邻职责：窗口/事件语义见 [Inno.Platform](Inno.Platform.md)，SDL3 映射见 [Inno.Adapter.Platform.Sdl3](Inno.Adapter.Platform.Sdl3.md)。
