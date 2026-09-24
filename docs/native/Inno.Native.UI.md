# Inno.Native.UI

[Native 索引](README.md) · [UI Toolchain](../build/Inno.Build.Toolchains.UI.md)

手写的 `Native/include/RmlUiRuntime.hpp` 是唯一语义边界：它用 PImpl 与中立 DTO 隐藏 RmlUi。本项目 `Bindings/rmlui.bridge.json` 驱动 BindGen-CS Cpp2C 生成 `Native/Generated/` C ABI，随后 `Bindings/bindgen.json` 从 C header 生成本项目的 `Generated/Bindings.cs`。两个生成器使用独立目录，避免原子替换输出时互相删除。桥接库固定使用 RmlUi 6.0 源码与对应 Text/字体依赖，产物为配置专属 `inno-ui` 动态库。原生 handle 只由 UI adapter 持有；游戏脚本及渲染 Plugin 消费 `Inno.UI` 中立数据，不直接调用 ABI。

## ABI、初始化与失败

公开的 `UiNativeConfig.AotStaticLink` 选择静态 AOT 符号解析；`UiNative.GetLibraryName()` 返回稳定库 stem。BGCS 生成的 `UiNative` 覆盖 runtime/context/document 生命周期、DOM 修改、输入、字体、命名纹理、帧几何、裁剪、像素及事件复制；`InnoUi*` 数据结构只为 ABI 使用。异常文本由 Cpp2C 的 thread-local last-error 通道传递，稳定 `Result` 不泄漏 RmlUi 名称。动态库由 `NativeDllLoader` 从目标 `.lib/ui/<platform>` 或 Player Support Pack 加载，缺库和 ABI 错误直接失败。

RmlUi 字体引擎是进程级资源；Context 销毁时释放文档与像素，但仍可能在最后一个 backend 关闭时收到字体纹理释放回调，因此原生层仅保留渲染接口壳直至 `Rml::Shutdown`。Context/Document ID 在进程内单调分配以拒绝跨 backend 旧句柄。参见 [UI adapter](../ui/Inno.Adapter.UI.RmlUi.md) 和 [绑定验收工具](../build/Inno.Build.NativeBindings.md)；macOS ARM64 已验证，其他平台需目标宿主验收。
