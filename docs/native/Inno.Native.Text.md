# Inno.Native.Text

[Native 索引](README.md) · [Text Toolchain](../build/Inno.Build.Toolchains.Text.md)

BindGen-CS 从本项目 `Native/inno_text.h` 生成 C# binding，目标文件位于 `Generated/Bindings.cs`。本项目的 `Native/` 包含手写 Text 语义桥和 C ABI 实现，以 FreeType 与 HarfBuzz 构建，并按 Debug/Release 提供 `inno-text` 动态库。ABI 只供 Text adapter/native tests 使用；脚本及业务代码不得引用。升级头文件或 vendor 后，必须重新生成绑定并构建同目标桥接库。

## ABI、初始化与失败

公开的 `TextNativeConfig.AotStaticLink` 选择静态 AOT 符号解析；`TextNative.GetLibraryName()` 返回稳定库 stem。BGCS 生成的 `TextNative` 暴露 context 创建/销毁、face 加载/释放、metrics、UTF-8 shaping、glyph rasterization 及结果消息；`InnoText*` 数据结构只为 ABI 使用，不是引擎脚本协议。动态库由 `NativeDllLoader` 从目标 `.lib/text/<platform>` 或 Player Support Pack 加载，缺库即启动失败，不提供伪排版结果。

`InnoTextContext` 与 face ID 由 [Text adapter](../text/Inno.Adapter.Text.FreeTypeHarfBuzz.md) 独占；调用方不能跨 context 保留原生指针。绑定配置在本项目 `Bindings/` 中，固定入口与 `Native/inno_text.cpp` 必须同源；使用 [绑定验收工具](../build/Inno.Build.NativeBindings.md) 校验生成文件和当前宿主的 ABI。macOS ARM64 已验证，其他平台需在目标宿主完成生成与测试。
