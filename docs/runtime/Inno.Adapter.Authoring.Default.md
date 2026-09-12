# Inno.Adapter.Authoring.Default

[Runtime 索引](README.md) · [Runtime catalog](Inno.Adapter.Default.md) · [Presentation contract](../platform/Inno.Adapter.Presentation.md)

该项目是默认 Editor 的 authoring implementation catalog。`DefaultAuthoringAdapterCatalog` 委托 `DefaultAdapterCatalog` 提供 runtime families，并额外实现：

- `IRenderingAuthoringBackendFactory`：BGFX shaderc/texturec toolchain。
- `IPresentationBackendFactory`：内部组合 SDL3 ImGui platform bridge 与 BGFX ImGui renderer。

EditorHost 只依赖 `IAuthoringAdapterCatalog`，不会看到 `Sdl3PlatformApplication`、`BgfxDevice`、`MiniAudioDevice` 或 `PlatformImGuiContext`。该 catalog 属于 Editor/authoring 发布闭包，不得进入 Player closure。

```csharp
IAuthoringAdapterCatalog catalog = new DefaultAuthoringAdapterCatalog();
```

构造参数 `renderingProviders` 与 `authoringProviders` 分别接收完整 runtime/authoring provider 集合，
null 使用对应内置 BGFX 集合。`RenderingAuthoringBackendCatalog` 在创建 GPU 设备前要求两边的
`RenderingBackendId` 集合完全配对，拒绝重复或缺失。Shader/Texture 工厂按稳定 ID 查找，不再维护
中央 Rendering enum switch。实际设备能力与 Shader 绑定/产物兼容性仍由各自后续验证负责。

Presentation 创建是原子操作：shader、renderer 或 context 任一步失败都释放已创建资源并让 Editor 启动失败；不会发布半初始化 context。
