# Native binding generation

[Native 索引](README.md) · [构建验收](../build/Inno.Build.NativeBindings.md) · [Wiki 首页](../README.md)

每个 `Inno.Native.XXX` 项目独立拥有 `Bindings/bindgen.json` 和两个 MSBuild 入口。生成结果固定为同一项目的 `Generated/Bindings.cs`；普通 `dotnet build` 只编译已生成文件，不会隐式修改它。C# 输出路径不按平台分目录，文件头的 `ABI reference target` 只记录解析所用目标，不代表其他目标已通过 ABI 验收。

以 SDL3 为例，在仓库根目录运行：

```shell
dotnet build native/Inno.Native.Sdl3/Inno.Native.Sdl3.csproj -t:GenerateBindings -c Release
dotnet build native/Inno.Native.Sdl3/Inno.Native.Sdl3.csproj -t:CheckBindings -c Release
dotnet build native/Inno.Native.Sdl3/Inno.Native.Sdl3.csproj -c Release
```

其他项目只需替换 `.csproj` 路径。`GenerateBindings` 运行该项目的 BGCS 配置；`CheckBindings` 在临时目录重新生成并比较当前文件，不改写 `Generated/`。默认查找同级的 `BindGen-CS` 仓库；若位置不同，加上 `-p:BindGenRoot=/absolute/path/to/BindGen-CS`。SDK host 不在 `PATH` 时，可设置 `-p:BindGenDotNetHost=/absolute/path/to/dotnet`。

| 项目 | 项目内配置 | 项目内特殊扩展 |
| --- | --- | --- |
| `Inno.Native.Bgfx` | `Bindings/bindgen.json` | `Bindings/bgfx-api-shape.json`、`bgfx-macro-enums.json` |
| `Inno.Native.ImGui` | `Bindings/bindgen.json` | `Bindings/cimgui-api-shape.json`、`Bindings/Extension/` |
| `Inno.Native.ImGuizmo` | `Bindings/bindgen.json` | 无 |
| `Inno.Native.MiniAudio` | `Bindings/bindgen.json` | 无 |
| `Inno.Native.Sdl3` | `Bindings/bindgen.json` | 无 |
| `Inno.Native.Text` | `Bindings/bindgen.json` | 无 |
| `Inno.Native.UI` | `Bindings/bindgen.json` | `Bindings/rmlui.bridge.json` |

ImGui 的 `Bindings/Extension` 是仅在生成/检查时构建的 BGCS 插件项目；`ImVector<T>` 与 `ImTextureID` 的 ABI 实现作为模板合入同一份生成文件，不进入正常 Native 项目的源码列表。Text 的原生实现归属 `Inno.Native.Text/Native/`。UI 项目的 `GenerateBindings` 先用本项目的 bridge 配置从 `Inno.Native.UI/Native/include/RmlUiRuntime.hpp` 生成 C ABI，再生成本项目的 C# 文件；原生桥产物归属 `Inno.Native.UI/Native/Generated/`，不属于 managed binding。

`Native/` 只放原生源代码与必要的生成桥，不放 CMake 构建目录。Text/UI 的原生中间产物分别写入对应 `Inno.Build.Toolchains.XXX/obj/native/`；最终动态库写入 `.lib/<domain>/<platform>/`，由 `NativeDllLoader` 按现有搜索规则部署和加载。

所有 API 命名、枚举、marshalling 与 ownership 约束应写在所属项目的配置或扩展中。不要修改 `Generated/Bindings.cs`，也不要在项目里另写 native import。完整原生依赖、solution、ABI 和运行时测试使用 [Inno.Build.NativeBindings](../build/Inno.Build.NativeBindings.md)；一个主机的通过结果不能替代其他平台的实机报告。
