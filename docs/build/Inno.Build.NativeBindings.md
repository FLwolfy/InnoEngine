# Inno.Build.NativeBindings

[Build 索引](README.md) · [Native 索引](../native/README.md) · [绑定生成说明](../native/BindingGeneration.md)

这是 InnoEngine 的原生绑定整体验收入口。它使用当前选定的 BindGen-CS checkout，不固定预发布提交，也不创建临时 Git worktree。每个 `Inno.Native.XXX` 项目自行提供 `GenerateBindings` 与 `CheckBindings`；验收器逐项目运行检查，并验证 RmlUi Cpp2C 桥确定性、手写 native import 缺失、原生依赖构建、完整 solution 构建和 Native/Text/UI 测试。

```shell
dotnet build native/Inno.Native.Sdl3/Inno.Native.Sdl3.csproj -t:GenerateBindings -c Release
dotnet build native/Inno.Native.Sdl3/Inno.Native.Sdl3.csproj -t:CheckBindings -c Release
dotnet run --project build/verification/Inno.Build.NativeBindings
```

默认使用同级 `BindGen-CS` 仓库。单项目生成/检查可设置 `-p:BindGenRoot=<dir>` 和 `-p:BindGenDotNetHost=<path>`；验收器接受 `--bindgen-root <dir>`、`--dotnet <path>` 与 `--configuration Debug|Release`。当前 BGCS CLI 与 `BGCS.Runtime` 项目都必须存在。验收不负责远端发布，也不会修改 BGCS 仓库。

每个受管绑定项目仅编译一个平台无关的 `Generated/Bindings.cs` 和一个 DLL 加载器。RmlUi 的原生 C 桥位于 `native/Inno.Native.UI/Native/Generated/`，不是额外的 managed binding。`artifacts/bindings/acceptance/<host>/` 下的 JSON/Markdown 报告只证明报告所列主机上的运行结果；其他平台必须独立实机验收。报告不会因某个平台文件夹存在就自动标为通过。

任一检查或测试失败都会返回非零状态，不写通过报告。生成命令会替换对应项目的 `Generated/` 内容；运行前应确认该目录没有需要保留的手工修改。
