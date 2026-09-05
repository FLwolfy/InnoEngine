# Inno.Native.MiniAudio

[Native 索引](README.md) · [MiniAudio Toolchain](../build/Inno.Build.Toolchains.MiniAudio.md) · [Wiki 首页](../README.md)

该项目提供 miniaudio 0.11.25 的完整 generated C ABI binding。公开面包含 `MiniAudio` 函数表、`Ma*` enum、struct、delegate 与 pointer wrapper；精确成员以同项目生成源码和 XML 文档为准。

## 职责与边界

- `MiniAudioConfig.AotStaticLink` 必须在首次访问 `MiniAudio` 前设置；默认动态模式从引擎 native 输出或 Player Support Pack 装载当前配置的动态库。
- `MiniAudio.GetLibraryName()` 返回平台无关的 `miniaudio` library stem。
- Debug 与 Release 分别绑定 `miniaudio-debug`、`miniaudio-release`。macOS 的 `lib` 前缀及扩展名由 `Inno.Native.LibraryLoading` 解析。
- 该程序集是后端 ABI，不是稳定游戏音频 API。业务、Scene、Asset 与脚本不得直接依赖；未来由 `Inno.Audio.MiniAudio` adapter 隔离。

## 生成来源与生命周期

Binding 由 BindGen-CS commit `3e4bf6a` 从 `extern/miniaudio/miniaudio.h` 生成，native library 与 managed binding 必须始终来自同一个 miniaudio commit。当前 vendor 固定为 tag `0.11.25`、commit `9634bedb5b5a2ca38c1ee7108a9358a4e233f14d`；升级时直接重新生成当前 API，不保留旧 ABI 兼容层。

当前 generated function table 固定包含 931 个 C ABI symbol，并与同一版本的参考 binding API 集合一致。首次调用任一 generated 函数会装载整个 function table；缺失任何 native export 都会立即失败。调用方必须遵守 miniaudio 原生的 init/uninit、线程与 callback 生命周期，不得跨插件代际持有 native pointer、delegate 或运行时对象。

当前发行目标只有 macOS ARM64 与 Windows x64；Linux 不在支持范围内。

## 基本验证

```csharp
using Inno.Native.MiniAudio;

uint major = 0;
uint minor = 0;
uint revision = 0;
MiniAudio.Version(ref major, ref minor, ref revision);
string version = MiniAudio.VersionStringS();
```

生产代码应通过未来的后端中立 Audio API 工作；上述调用仅用于 Native test 与底层 adapter 实现。
