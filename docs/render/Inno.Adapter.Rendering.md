# Inno.Adapter.Rendering

[Rendering 索引](README.md) · [中立 Rendering](Inno.Rendering.md) · [BGFX implementation](Inno.Adapter.Rendering.Bgfx.md)

该项目定义 Rendering Adapter family，不引用 BGFX Native。

## 公开 API

- `RenderingBackendId`：开放、区分大小写的稳定实现标识；`bgfx` 是默认值，不是支持名单。默认 struct 值未赋值，创建设备前拒绝。
- `RenderingBackendProvider`：一个实现的 `id` 与 `CreateDevice(options)`，由当前 Composition 持有。
- `RenderingBackendCatalog`：捕获完整 provider 集合，拒绝重复/空 ID；只解析明确注册的后端，不做静默替换。
- `RenderingBackendOptions`：中立 window、graphics API preference、VSync、sRGB 与 threading policy。
- `IRenderingBackendFactory.supportedBackends/CreateDevice`：Player 与 Shell 使用的 runtime-only device factory。Shell 在初始化窗口前检查所选 ID。
Authoring compiler factory 被隔离在 [Inno.Adapter.Rendering.Authoring](Inno.Adapter.Rendering.Authoring.md)。
`Inno.Adapter.Default` 只依赖本项目，防止 Player 闭包间接带入 `Inno.Rendering.Assets`、AssetPipeline 与 Build Toolchain。

```csharp
IRenderDevice device = catalog.rendering.CreateDevice(
    selection.rendering,
    new RenderingBackendOptions { window = window });
```

BGFX handle、view ID、native enum 和 compiler executable path 不得进入这些公开契约。

自定义后端通过 `new RenderingBackendId("studio.rendering.custom")` 与派生 provider 注册；不修改公共枚举或中央分支。
`DefaultAdapterCatalog(renderingProviders)` 接收完整替换集合，null 才使用默认 BGFX。Provider 属于 Composition generation，不在跨代静态缓存中保留。
