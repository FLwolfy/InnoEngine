# Inno.Build.Toolchains.Bgfx

[Build 索引](README.md) · [BGFX Native](../native/Inno.Native.Bgfx.md)

这是 BGFX native library 与工具的构建 CLI，不提供稳定 library API。命令按当前 host/target 与 debug/release 配置生成 `.lib/bgfx/<rid>` 产物，供 Editor 开发运行和 Support Pack 生产使用。

它允许引用 BGFX Native/Tools 与公共 Toolchains，但不得被 Runtime、Editor feature 或 Player 引用。
