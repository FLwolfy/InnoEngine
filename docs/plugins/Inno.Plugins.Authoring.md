# Inno.Plugins.Authoring

[Plugins 索引](README.md) · [Assets](../assets/Inno.Assets.Pipeline.md) · [Build](../build/Inno.Build.md)

## 职责与依赖

该 project 发现 `Plugins/*.zip` 与 `Plugins/<folder>`，执行相同安全校验、依赖拓扑、只读 Asset Source Mount 候选和原子 Plugin generation 激活。它属于 Editor/Build authoring closure，不进入 Player。

## 公开 API

- `PluginEnvironment`：active/discovery generation、pending code activation、owner-thread Update/Refresh。
- `PluginSourceService`：一次隔离 Scan 与 ZIP/folder validation。
- `PluginSourceKind`, `PluginSourceLimits`：安装源种类和资源上限。
- `PluginCandidate`, `PluginDiagnostic`, `PluginScanResult`：不可变候选与诊断模型。

## 工作流

Application 先 Scan，再以相同结果构造 Asset source mounts 和 `PluginEnvironment`。ZIP 与 Folder 安装源都会先物化到 `<Project>/Library/Plugins/<pluginId>/<contentHash>` 的不可变、内容寻址 generation snapshot；active 与 candidate Source Mount 只读取各自 snapshot，绝不直接挂载仍可能被外部删除或原地改写的 `Plugins/<folder>/Assets`。因此安装源在复制期间变化、被删除或路径类型改变时，不会令正在读取的 generation、事务 rollback 或 Editor shutdown 重新访问失效路径。

文件系统 watcher 只合并变化信号；候选激活回到 Asset owner thread。不可变 old snapshot 的用途是保证原子事务在真正的 participant/迁移异常时能够完整 rollback，它不用于否认安装集合已经发生的删除或不可用更新。若 active Plugin 被物理删除、结构校验失败、Asset metadata 无法构成候选，或更新后的代码无法完成全量脚本编译，Scripting 会在帧安全点提交 unavailable generation：退休该 Plugin module 及其完整反向依赖脚本闭包，并提交与当前安装内容一致的只读 Mount/Catalog/Settings。普通、与 Plugin availability 无关的 Project C# 编译失败仍不会改变 active generation。

Unavailable generation 中的 Scene Component/System 会原位转换为 `MissingGameComponent` / `MissingGameSystem`，保留 Stable Type ID、persistent ID、序列化状态和顺序；Project Scripts 因缺失 Plugin API 而无法重建时，其所在脚本 module 同样退休并进入 Missing。相同 Stable ID 的有效 Plugin 和 Project Scripts 再次编译成功后，下一次原子 reload 自动恢复真实类型和保留状态。结构或 Asset 候选失败会发布 discovery diagnostic，不会从 `Refresh`、File Browser、reload rollback 或 shutdown 抛出安装目录失效异常。

ZIP 路径、entry 数、压缩比、大小、case collision、symbolic link、重复 ID、缺失依赖和循环均在 candidate 阶段拒绝。安装源永远只读，修改必须通过外部替换。
