# Inno.Build.Toolchains.Bgfx.Tools

[Build 索引](README.md) · [Rendering Assets](../render/Inno.Rendering.Assets.md)

## 公开 API

- `BgfxShadercToolchain`、`BgfxShaderTargetPlatform`：把共享 Shader IR 编译为目标 backend artifact。
- `BgfxTextureTargetCompiler`：把创作纹理离线编译为 portable KTX。
- `BgfxGameContentCompiler`：遍历目标构建 snapshot 并写入 `TargetArtifacts`。

这些类型只在 authoring/build 路径使用。Player 通过 `FileRenderTargetArtifactProvider` 读取结果，不引用本项目或 BGFX tools。
