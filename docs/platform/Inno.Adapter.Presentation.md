# Inno.Adapter.Presentation

[Platform 索引](README.md) · [Rendering](../render/README.md) · [Runtime adapters](../runtime/Inno.Adapter.md)

该项目定义 authoring product 的图形 Presentation family。它不暴露 SDL3 window、BGFX handle 或具体 ImGui context。

## 公开 API

- `PresentationBackend`、`PresentationFeatures`：Editor 启动时选择实现与声明中立 capability。
- `PresentationBackendOptions`：组合中立 platform/window/render device、shader compiler 与 asset source。
- `PresentationTextureHandle`：presentation generation 内使用的 opaque texture token。
- `IPresentationContext`：layout、frame draw、texture registration 与 render-graph contribution。
- `IPresentationBackendFactory`：创建 presentation context。
- `IAuthoringAdapterCatalog`：在 runtime `IAdapterCatalog` 上增加 rendering authoring 与 presentation factory。

EditorHost 只保存 `IPresentationContext`。SDL3 event bridge 与 BGFX ImGui renderer 在 `Inno.Adapter.Authoring.Default` 内组合，不能出现在 Host 的字段、参数、返回值或 using 中。

```csharp
using IPresentationContext presentation = authoringCatalog.presentation.CreateContext(
    PresentationBackend.ImGui,
    options);
```

Presentation 是 Application 生命周期资源，必须先于 shared render device 释放。
