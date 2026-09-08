# Inno.Build.Toolchains

[Build 索引](README.md) · [Native](../native/README.md)

## 公开 API

- `ToolchainEnvironment`：建立受控工具进程环境。
- `BuildArtifactOptions`：描述 debug/release 与目标 artifact 选择。
- `BuildArtifactCopier`：复制明确的 Native 产物。
- `ToolchainLayout`：解析仓库内 toolchain/source/output 布局。

该项目只服务构建机器，不进入 Runtime 或 Player。路径缺失、产物歧义和进程失败必须明确抛出，不使用 fallback tool location。
