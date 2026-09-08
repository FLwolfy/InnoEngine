# Inno.Adapter.Authoring.Default

[Runtime 索引](README.md) · [Runtime catalog](Inno.Adapter.Default.md) · [Presentation contract](../platform/Inno.Adapter.Presentation.md)

该项目是默认 Editor 的 authoring implementation catalog。`DefaultAuthoringAdapterCatalog` 委托 `DefaultAdapterCatalog` 提供 runtime families，并额外实现：

- `IRenderingAuthoringBackendFactory`：BGFX shaderc/texturec toolchain。
- `IPresentationBackendFactory`：内部组合 SDL3 ImGui platform bridge 与 BGFX ImGui renderer。

EditorHost 只依赖 `IAuthoringAdapterCatalog`，不会看到 `Sdl3PlatformApplication`、`BgfxDevice`、`MiniAudioDevice` 或 `PlatformImGuiContext`。该 catalog 属于 Editor/authoring 发布闭包，不得进入 Player closure。

```csharp
IAuthoringAdapterCatalog catalog = new DefaultAuthoringAdapterCatalog();
```

Presentation 创建是原子操作：shader、renderer 或 context 任一步失败都释放已创建资源并让 Editor 启动失败；不会发布半初始化 context。
