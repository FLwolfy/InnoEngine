# Inno.Build.Toolchains.UI

[Build 索引](README.md) · [Native UI](../native/Inno.Native.UI.md)

CLI 以 `native/Inno.Native.UI/Native` 为只读 CMake 源目录，在本 toolchain 项目的 `obj/native/<platform>/<config>/` 构建，复制配置专属库到 `.lib/ui/<platform>/`。本机命令：`dotnet run --project build/toolchains/Inno.Build.Toolchains.UI -- build --config debug`（或 `release`）。`clean` 仅清除此 toolchain 的中间产物与最终库。支持 macOS ARM64、Windows x64、Linux x64/ARM64；目标平台需分别构建和验收。

## 操作与交付边界

CLI 的公开命令为 `build --config debug|release` 与 `clean`。`build` 使用已固定的 `extern/RmlUi`、FreeType、HarfBuzz、`native/Inno.Native.UI/Native/src` 和已签入的 `Native/Generated`，编译后只复制该配置的 `inno-ui` 动态库到重建型 `.lib`；它不会在普通构建中隐式改写 binding。修改 facade 后通过 [Inno.Native.UI 项目](../native/BindingGeneration.md)运行 BGCS 生成目标。构建失败保留 CMake/编译诊断，不生成空的成功产物。Release Player Support Pack 需要对应目标的 release 库，见 [Support Packs](Inno.Build.SupportPacks.md)。

```bash
dotnet run --project build/toolchains/Inno.Build.Toolchains.UI -- build --config release
```

本机验证后还需用 [BGCS 验收](Inno.Build.NativeBindings.md) 检查 ABI 与 managed solution；Linux/Windows 不能以 macOS 的成功结果代替目标验收。
