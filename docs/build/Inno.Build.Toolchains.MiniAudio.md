# Inno.Build.Toolchains.MiniAudio

[Build 索引](README.md) · [MiniAudio Native](../native/Inno.Native.MiniAudio.md) · [Support Packs](Inno.Build.SupportPacks.md)

这是 miniaudio 共享库构建 CLI，不提供稳定 library API。它只处理固定的 `extern/miniaudio` checkout，并把规范化产物写入 `.lib/miniaudio/<rid>`。

## 构建范围

当前支持 macOS ARM64 与 Windows x64，不提供 Linux builder。构建启用完整标准 miniaudio engine、device I/O、decoder、resource manager 和 node graph；examples、tests、tools 与额外 node libraries 不进入产物。`MA_DLL` 用于导出与 generated function table 对应的 C ABI。

```shell
/path/to/dotnet run --project build/toolchains/Inno.Build.Toolchains.MiniAudio -- build --config debug
/path/to/dotnet run --project build/toolchains/Inno.Build.Toolchains.MiniAudio -- build --config release
/path/to/dotnet run --project build/toolchains/Inno.Build.Toolchains.MiniAudio -- clean
```

输出分别为：

- macOS：`.lib/miniaudio/osx-arm64/libminiaudio-debug.dylib` 与 `libminiaudio-release.dylib`
- Windows：`.lib/miniaudio/windows-x64/miniaudio-debug.dll` 与 `miniaudio-release.dll`

`clean` 只删除 miniaudio 的 dependency-local build 目录和 `.lib/miniaudio`。源码缺失、host/architecture 不支持、CMake 失败或没有发现共享库时，命令返回非零状态；不会创建静默 fallback。
