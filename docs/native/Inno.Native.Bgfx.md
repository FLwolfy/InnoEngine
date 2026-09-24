# Inno.Native.Bgfx

[Native 索引](README.md) · [Rendering BGFX](../render/Inno.Adapter.Rendering.Bgfx.md)

该项目提供 BGFX C API 的 generated binding surface。`Generated/Bindings.cs` 由 BGCS 生成 handle、enum、descriptor、callback 和 API；`bgfx.cs` 只负责原生库加载。完整成员契约由同项目生成 XML 记录，Wiki 不把生成实现提升为引擎稳定领域 API。

`Tools/` 归属同一项目，提供 `BgfxTool` 工具标识、`ToolRunner` 的安全参数化进程调用，以及带退出码、stdout、stderr 的 `ToolRunResult`。工具执行器借助 `NativeDllLoader` 查找或部署 `.lib/bgfx/<platform>` 中的工具，不改变 generated binding；Shader/Texture 的编译策略仍归属 [BGFX build toolchain](../build/Inno.Build.Toolchains.Bgfx.Tools.md)，不放在 Native 中。

只有 `Inno.Adapter.Rendering.Bgfx`、BGFX toolchain 和 Native tests 可以引用本项目。上层 public/protected API 不得泄漏任一 BGFX 类型或原生指针。
