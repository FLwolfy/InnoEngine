# Inno.Build.Cli

[Build 索引](README.md) · [Inno.Build](Inno.Build.md)

这是 headless Build Composition Root，不提供稳定 library API。它负责解析命令行、创建 Engine/authoring services、选择平台 target 并调用同一 `BuildPipeline`。

Game 命令未提供 `--profile` 时，从项目根 `Settings.Build.inno` 复制 Game 默认值；文件尚不存在时使用与 Editor 相同的项目派生默认值。`--profile <BuildProfile.inno>` 是显式 one-off 参数入口，不会改写 `Settings.Build.inno`，其中的 Application ID 会被当前 Project ID 统一替换。Plugin 命令同样直接使用当前 Project ID，不再接受 `--plugin-id`。CLI 的 `--output` 仍是本次命令的输出位置。

CLI 不包含独立构建算法；错误通过结构化 Build diagnostics 和非零进程退出码报告。它可以依赖 Build/Compiler/authoring projects，但不会进入 Player closure。
