# Inno.Native.Bgfx

[Native 索引](README.md) · [Rendering BGFX](../render/Inno.Rendering.Bgfx.md)

该项目提供 BGFX C API 的 generated/handwritten binding surface，核心公开入口是 `bgfx` partial static API 及生成的 handle、enum、descriptor 和 callback 类型。完整成员契约由同项目生成 XML 记录，Wiki 不把生成实现提升为引擎稳定领域 API。

只有 `Inno.Rendering.Bgfx`、BGFX toolchain 和 Native tests 可以引用本项目。上层 public/protected API 不得泄漏任一 BGFX 类型或原生指针。
