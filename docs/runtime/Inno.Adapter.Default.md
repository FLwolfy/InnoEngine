# Inno.Adapter.Default

[Runtime 索引](README.md) · [Adapter contract](Inno.Adapter.md) · [Shell](Inno.Shell.md)

该项目是标准发行版的 runtime implementation catalog。`DefaultAdapterCatalog` 实现 `IAdapterCatalog`，
提供 SDL3 platform/input、FileSystem storage、BGFX rendering 与 MiniAudio audio。
Rendering 使用开放的 `RenderingBackendId` 与 provider snapshot，其他 family 的选择协议不变。

具体类型只出现在该程序集内部的 factory 实现中。公开 surface 只有 `DefaultAdapterCatalog` 及其返回的中立 factory interface；创建方法通过 interface 调用。

```csharp
IAdapterCatalog catalog = new DefaultAdapterCatalog();
IRenderingBackendFactory rendering = catalog.rendering;
```

该项目禁止引用 Presentation、Build Toolchain、AssetPipeline、Compiler 或 Editor。它是 Player closure 的唯一默认 implementation 入口，后端初始化失败会原样终止启动或由对应 Runtime Service 进入明确 degraded state。

`DefaultAdapterCatalog(IEnumerable<RenderingBackendProvider>? renderingProviders = null)` 可接收完整替换
provider 集合，null 才使用内置 BGFX。它不是向默认列表追加项；重复/空 ID 在构造时失败，选择未注册
ID 在 Shell 创建窗口前失败。`platform`、`input`、`storage`、`rendering`、`audio` 属性仍返回中立工厂。
Provider 由 composition owner 持有，不参与 gameplay 插件热替换。
