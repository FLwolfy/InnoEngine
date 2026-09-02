# Inno.Build.Cli

[Build 索引](README.md) · [Inno.Build](Inno.Build.md)

这是 headless Build Composition Root，不提供稳定 library API。它负责解析命令行、创建 Engine/authoring services、加载 `BuildProfile.inno`、选择平台 target 并调用同一 `BuildPipeline`。

CLI 不包含独立构建算法；错误通过结构化 Build diagnostics 和非零进程退出码报告。它可以依赖 Build/Compiler/authoring projects，但不会进入 Player closure。
