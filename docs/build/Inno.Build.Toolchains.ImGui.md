# Inno.Build.Toolchains.ImGui

[Build 索引](README.md) · [ImGui Native](../native/Inno.Native.ImGui.md)

这是 cimgui native bridge 构建 CLI，不提供稳定 library API。产物只服务 Editor/ImGui 组合，不进入 headless Build 或普通 Player closure。

## 连续窗口背景

macOS/Windows builder 共用 `CimguiSourceOverlay`：读取当前 submodule 的 `imgui.cpp`，严格验证两处
窗口背景绘制锚点，在 `extern/cimgui/bld/inno-source` 生成构建专用源码。它使非停靠窗口先绘制一块
覆盖完整窗口的圆角背景，再叠加标题栏；停靠背景逻辑不变。外沿圆角和抗锯齿保持启用，内部标题/正文
交界不再由两块各自半透明的抗锯齿边缘拼接。

原始第三方 checkout 不修改；CMake wrapper 保留上游 target 的导出、编译宏和依赖，只替换此编译单元。
上游实现不再匹配时构建明确失败，不能静默省略修复。产物仅从本次平台的 `bld/inno/<platform>` 收集，
防止其他构建目录的旧库覆盖当前输出。Debug/Release 都必须通过此 CLI 构建，不能直接分发未修正的上游库。

```shell
dotnet run --project build/toolchains/Inno.Build.Toolchains.ImGui -- build --config debug
dotnet run --project build/toolchains/Inno.Build.Toolchains.ImGui -- build --config release
```

`InspectorPresentationLayoutTests` 通过公开 native draw list 的真实三角形与顶点 alpha 检查交界处连续覆盖，
并保留关闭按钮/Tooltip/HelpBox 回归。该实现不改变 native ABI，也不涉及 Rendering Core 或 2D 语义。
