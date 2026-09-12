# Inno.Adapter.Rendering.Authoring

[Rendering 索引](README.md) · [Runtime adapter contract](Inno.Adapter.Rendering.md) · [默认 Authoring catalog](../runtime/Inno.Adapter.Authoring.Default.md)

该项目把 rendering authoring compiler contract 与 runtime device factory 分离，只进入 Editor/authoring 闭包。

## 公开 API

- `IRenderingAuthoringBackendFactory.CreateShaderCompilerToolchain`：创建与所选 runtime backend 匹配的 shader target compiler。
- `IRenderingAuthoringBackendFactory.CreateTextureTargetCompiler`：创建匹配的 texture target compiler。

接口使用开放的 `RenderingBackendId` 选择实现，并返回 `Inno.Rendering.Assets` 中的中立 compiler contract。具体 shaderc/texturec 工具、路径与进程实现只存在于默认 Adapter 及对应 Toolchain。

`RenderingAuthoringBackendProvider` 声明 `id`、`CreateShaderCompilerToolchain()` 和 `CreateTextureTargetCompiler()`。
`RenderingAuthoringBackendCatalog(runtime, providers)` 捕获不可变注册集合，并在创建 native device 前校验 runtime 与 authoring ID 集合完全一致；空 ID、重复 ID 或未配对集合抛出 `ArgumentException`。
`supportedBackends` 提供当前注册集合；未注册 ID 的创建操作明确失败。
`DefaultAuthoringAdapterCatalog(renderingProviders, authoringProviders)` 支持完整自定义配对集合；两者均 null 使用内置 BGFX。

该程序集允许依赖 `Inno.Rendering.Assets`，但 `Inno.Adapter.Rendering`、`Inno.Adapter`、`Inno.Adapter.Default`、`Inno.Shell` 与 Player 均不得反向引用它。
