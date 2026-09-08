# Inno.Plugins.Authoring

[Plugins 索引](README.md) · [Assets](../assets/Inno.Assets.Pipeline.md) · [Build](../build/Inno.Build.md)

## 职责与依赖

该 project 只发现 `Plugins/*.iplugin`，执行 archive 安全校验、依赖拓扑、只读 Asset Source Mount 候选和原子 Plugin generation 激活。Folder Plugin、`.zip` 文件和其他扩展名会产生安装诊断，不进入候选。该 project 属于 Editor/Build authoring closure，不进入 Player。

## 公开 API

内部依赖 Core Execution 的统一退休异常分类；不会向游戏脚本暴露底层退休调度器或原生 callback。

- `PluginEnvironment`：active/discovery generation、pending code activation、owner-thread Update/Refresh。
- `PluginSourceService`：一次隔离 Scan 与 `.iplugin` validation。
- `PluginSourceKind`, `PluginSourceLimits`：安装源种类和资源上限。
- `PluginCandidate`, `PluginDiagnostic`, `PluginScanResult`：不可变候选与诊断模型。

## 工作流

Application 先 Scan，再以相同结果构造 Asset source mounts 和 `PluginEnvironment`。`.iplugin` 会先物化到 `<Project>/Library/Plugins/<pluginId>/<contentHash>` 的不可变、内容寻址 generation snapshot；active 与 candidate Source Mount 只读取各自 snapshot。因此安装包被删除或原子替换时，不会令正在读取的 generation、事务 rollback 或 Editor shutdown 重新访问失效路径。

文件系统 watcher 只合并变化信号；候选激活回到 Asset owner thread。不可变 old snapshot 的用途是保证原子事务在真正的 participant/迁移异常时能够完整 rollback，它不用于否认安装集合已经发生的删除或不可用更新。若 active Plugin 被物理删除、结构校验失败、Asset metadata 无法构成候选，或更新后的代码无法完成全量脚本编译，Scripting 会在帧安全点提交 unavailable generation：退休该 Plugin module 及其完整反向依赖脚本闭包，并提交与当前安装内容一致的只读 Mount/Catalog/Settings。普通、与 Plugin availability 无关的 Project C# 编译失败仍不会改变 active generation。

Unavailable generation 中的 Scene Component/System 会原位转换为 `MissingGameComponent` / `MissingGameSystem`，保留 Stable Type ID、persistent ID、序列化状态和顺序；Project Scripts 因缺失 Plugin API 而无法重建时，其所在脚本 module 同样退休并进入 Missing。相同 Stable ID 的有效 Plugin 和 Project Scripts 再次编译成功后，下一次原子 reload 自动恢复真实类型和保留状态。结构或 Asset 候选失败会发布 discovery diagnostic，不会从 `Refresh`、File Browser、reload rollback 或 shutdown 抛出安装目录失效异常。

Archive 路径、entry 数、压缩比、大小、case collision、symbolic link、重复 ID、缺失依赖和循环均在 candidate 阶段拒绝。内嵌依赖也必须是 `Dependencies/<id>.iplugin`。安装源永远只读，修改必须通过外部替换完整 `.iplugin` 文件。

## 退出与异常边界

普通输入校验失败可以进入 unavailable 候选，但直接或嵌套在 `AggregateException` / `InnerException` 中的
`RetirementPendingException` 不属于可隔离的输入错误。初始激活、候选准备、激活和观察者通知都必须立即传播该信号；
不能再调用后续观察者，不能自动回滚仍在使用的候选，也不能以空 Plugin 集合继续启动。普通错误先发生、之后退休阻塞时，
两者保留在同一异常树中，不丢失最初的激活/回滚错误。

`RollbackPending()` 只有在 Asset、Catalog 和 Settings contributor 回滚全部返回后才清空候选所有权。
回滚中断时 `hasPendingActivation` 和 `compilationAssets` 仍指向原事务；`Dispose()` 也不能越过该事务提前拆卸其他依赖。
外层 generation owner 仍负责统一 Fault / GC unload barrier；这不是允许在 Faulted Host 内重新发起 generation 的入口。
构造器在初始激活成功后才创建 watcher，避免激活失败时遗留不可达的文件系统订阅。

## 共享 Recovery 与后台工作

`PluginEnvironment` 构造注入 Host 唯一的 `GenerationCoordinator`。`CreateReloadChange()` 返回真实的 ReferenceRecoveryTransaction：Prepare 捕获 Settings neutral effective snapshot，Apply 发布 mount/catalog/contributor 并更新 Assets/Settings，Validate 检查 source 身份与 owner 恢复结果，Complete 提交，失败先恢复旧类型发布再恢复旧内容。

有代码变化时由 IScriptReloadCoordinator.Execute(reload, externalChange) 把同一个 change 与 Editor participant 合并；无代码变化的安装内容使用 ApplyPendingChange() 经过同一 gate。低层 ActivatePending/CommitPending/RollbackPending 只用于组装该事务，Activate 失败不自行抢先补偿。没有两段 Action callback 或按领域中央 switch。

Plugin/Setting ID 是稳定语义，不能伪造 object Identity；mount 内实际 Asset slots 仍走共享 ReferenceRecoveryTransaction。Settings、Graph 的中立结构 participant 可以没有 object slots，但必须真实捕获/验证/回滚其领域状态。

后台 reconciliation 属于 LifetimeScope.RunAsync，预期 IO 变化返回中立重试结果；停止时先取消/排空，确认 Task 退休后才拆 watcher、sources、catalog。不得遗留未拥有的 Task.Run 或复制 AsyncLocal generation 根。PluginCandidate.manifest 返回深复制，Scan/Catalog 列表冻结；修改外部记录不会修改已发布 generation。
