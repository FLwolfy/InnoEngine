# Inno.Adapter.Rendering

[Rendering 索引](README.md) · [中立 Rendering](Inno.Rendering.md) · [BGFX implementation](Inno.Adapter.Rendering.Bgfx.md)

该项目定义 Rendering Adapter family，不引用 BGFX Native。

## 公开 API

- `RenderingBackend`：Composition 启动时使用的 rendering implementation 选择。
- `RenderingBackendOptions`：中立 window、graphics API preference、VSync、sRGB 与 threading policy。
- `IRenderingBackendFactory.CreateDevice`：Player 与 Shell 使用的 runtime-only device factory。
Authoring compiler factory 被隔离在 [Inno.Adapter.Rendering.Authoring](Inno.Adapter.Rendering.Authoring.md)。
`Inno.Adapter.Default` 只依赖本项目，防止 Player 闭包间接带入 `Inno.Rendering.Assets`、AssetPipeline 与 Build Toolchain。

```csharp
IRenderDevice device = catalog.rendering.CreateDevice(
    selection.rendering,
    new RenderingBackendOptions { window = window });
```

BGFX handle、view ID、native enum 和 compiler executable path 不得进入这些公开契约。
