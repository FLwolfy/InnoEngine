# Inno.Native.Bgfx.Tools

[Native 索引](README.md) · [BGFX Toolchain](../build/Inno.Build.Toolchains.Bgfx.Tools.md)

## 公开 API

- `BgfxTool`：受支持的 BGFX command-line tool identity。
- `ToolRunResult`：退出码、标准输出和错误输出。
- `ToolRunner`：从明确工具目录执行一次请求并返回完整结果。

该项目不决定 Shader/Texture 领域策略；它只封装进程执行。超时、缺失 executable 和非零退出均由上层 toolchain 转换为可定位 Build diagnostic。
