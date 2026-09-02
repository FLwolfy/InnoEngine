# Inno.Scripting.Reload

[Scripting 索引](README.md) · [Compiler](Inno.Scripting.Compiler.md) · [Extensibility](../extensibility/Inno.Extensibility.Modules.md)

## 公开 API

- `ScriptReloadHost`：把成功编译结果准备成 Module/Type/feature candidates；Plugin availability 改变而无法产出替代编译时，准备显式的 module retirement candidate；两者都只在安全点原子激活。
- `ScriptReloadOptions`：reload boundary 和诊断策略。
- `IScriptReloadCoordinator`：Editor/Scene/Rendering 等生产 feature 的事务协调 contract。

Reload 不重新编译源文件。普通 Project 编译失败没有 candidate，active generation 保持不变；但 active Plugin 被删除、结构失效或更新失败时，安装集合变化本身是有效事实，不能因为依赖脚本编译失败而保留旧 Plugin。Host 会以 active/candidate Plugin ID 与 content hash 计算 retired Plugin modules，再沿 `upstreamModuleNames` 取得完整反向依赖闭包，原子移除 Plugin、Runtime Scripts 和 Editor Scripts。Scene participant 在同一事务内把已退休类型变成保留 Stable ID 与状态的 Missing，并在类型返回时恢复。

只有 participant Prepare/Activate、Scene 状态迁移或外部 generation 同步本身失败时，事务才 rollback 到仍完整存在的 immutable previous snapshot。Complete 清理失败被诊断但不会撤销已经提交的 generation。调用者负责 Dispose host，释放 catalog registrations 与 retiring generation lease。
