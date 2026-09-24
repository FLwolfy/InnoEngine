# Inno.Build.Toolchains.Text

[Build 索引](README.md) · [Native Text](../native/Inno.Native.Text.md)

CLI 以 `native/Inno.Native.Text/Native` 为只读 CMake 源目录，在本 toolchain 项目的 `obj/native/<platform>/<config>/` 构建，复制配置专属库到 `.lib/text/<platform>/`。本机命令：`dotnet run --project build/toolchains/Inno.Build.Toolchains.Text -- build --config debug`（或 `release`）。`clean` 仅清除此 toolchain 的中间产物与最终库。支持 macOS ARM64、Windows x64、Linux x64/ARM64；跨平台产物需在目标环境验收。

## 操作与交付边界

CLI 的公开命令为 `build --config debug|release` 与 `clean`。`build` 使用已固定的 `extern/freetype`、`extern/harfbuzz` 和 `native/Inno.Native.Text/Native`，编译后只复制该配置的 `inno-text` 动态库到重建型 `.lib`；不修改 Project 源资产。构建失败保留明确 CMake/编译诊断，不生成空的成功产物。Release Player Support Pack 需要对应目标的 release 库，见 [Support Packs](Inno.Build.SupportPacks.md)。

```bash
dotnet run --project build/toolchains/Inno.Build.Toolchains.Text -- build --config release
```

本机验证后还需用 [BGCS 验收](Inno.Build.NativeBindings.md) 检查 ABI 与 managed solution；Linux/Windows 不能以 macOS 的成功结果代替目标验收。
