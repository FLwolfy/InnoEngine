# Inno.Adapter.Rendering.Authoring

[Rendering 索引](README.md) · [Runtime adapter contract](Inno.Adapter.Rendering.md) · [默认 Authoring catalog](../runtime/Inno.Adapter.Authoring.Default.md)

该项目把 rendering authoring compiler contract 与 runtime device factory 分离，只进入 Editor/authoring 闭包。

## 公开 API

- `IRenderingAuthoringBackendFactory.CreateShaderCompilerToolchain`：创建与所选 runtime backend 匹配的 shader target compiler。
- `IRenderingAuthoringBackendFactory.CreateTextureTargetCompiler`：创建匹配的 texture target compiler。

接口使用 `RenderingBackend` 选择实现，并返回 `Inno.Rendering.Assets` 中的中立 compiler contract。具体 shaderc/texturec 工具、路径与进程实现只存在于 `Inno.Adapter.Authoring.Default` 及对应 Toolchain。

该程序集允许依赖 `Inno.Rendering.Assets`，但 `Inno.Adapter.Rendering`、`Inno.Adapter`、`Inno.Adapter.Default`、`Inno.Shell` 与 Player 均不得反向引用它。
