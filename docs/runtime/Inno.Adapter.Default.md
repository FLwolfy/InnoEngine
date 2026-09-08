# Inno.Adapter.Default

[Runtime 索引](README.md) · [Adapter contract](Inno.Adapter.md) · [Shell](Inno.Shell.md)

该项目是标准发行版的 runtime implementation catalog。`DefaultAdapterCatalog` 实现 `IAdapterCatalog`，并把中立枚举映射到 SDL3 platform/input、FileSystem storage、BGFX rendering 与 MiniAudio audio。

具体类型只出现在该程序集内部的 factory 实现中。公开 surface 只有 `DefaultAdapterCatalog` 及其返回的中立 factory interface；创建方法通过 interface 调用。

```csharp
IAdapterCatalog catalog = new DefaultAdapterCatalog();
IRenderingBackendFactory rendering = catalog.GetRendering(
    AdapterSelection.defaultValue.rendering);
```

该项目禁止引用 Presentation、Build Toolchain、AssetPipeline、Compiler 或 Editor。它是 Player closure 的唯一默认 implementation 入口，后端初始化失败会原样终止启动或由对应 Runtime Service 进入明确 degraded state。
