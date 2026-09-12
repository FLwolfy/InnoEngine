# Inno.Build.Cli

[Build 索引](README.md) · [Inno.Build](Inno.Build.md)

这是 headless Build Composition Root，不提供稳定 library API。它负责解析命令行、创建 Engine/authoring services、选择平台 target 并调用同一 `BuildPipeline`。

Game 命令未提供 `--profile` 时，从项目根 `Settings.Build.inno` 复制 Game 默认值；文件尚不存在时使用与 Editor 相同的项目派生默认值。`--profile <BuildProfile.inno>` 是显式 one-off 参数入口，不会改写 `Settings.Build.inno`，其中的 Application ID 会被当前 Project ID 统一替换。Plugin 命令同样直接使用当前 Project ID，不再接受 `--plugin-id`。CLI 的 `--output` 仍是本次命令的输出位置。

CLI 不包含独立构建算法；错误通过结构化 Build diagnostics 和非零进程退出码报告。它可以依赖 Build/Compiler/authoring projects，但不会进入 Player closure。

Headless 构建先编译并激活完整 authoring generation，再对账资产，最后编译目标 Player scripts 和导出。
这样项目/插件的 `.editor.cs` importer、Shader 节点和语言扩展与 Editor 构建路径一致；编译失败直接终止，
不把缺失 importer 的 last-good 状态当作当前源码。CLI 只提供这些脚本需要的 Editor API 程序集元数据，
不启动 Editor UI。`Inno.Editor.Annotations` 只进入脚本编译引用，部署编译移除属性后不保留运行时引用。
这些 authoring 依赖不加入 Player support pack。

`--project` 接受绝对或相对目录路径，包括末尾带目录分隔符的写法。与 Editor 一样，初始 Project ID 从目录本身的名称派生，不把末尾分隔符解释为空项目名；已有项目设置中的 ID 保持不变。
