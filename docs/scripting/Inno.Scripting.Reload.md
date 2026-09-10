# Inno.Scripting.Reload

## 项目创作代际

同一项目的 GameScripts 与 EditorScripts 共同构成一个创作代际。即使只修改 Editor 源码，也一起经过候选、Scene/Settings 状态恢复、原子激活与严格卸载验证。原因是 Editor 中的泛型扩展闭合于项目 runtime 类型时，CLR loader allocator 可以形成双向依赖；单向程序集引用并不保证两个 collectible context 可独立卸载。编译仍可复用未修改的 GameScripts 产物，不要求重编全部源码。Plugin 仍按公开模块依赖图处理，没有任何 Rendering2D 特例。

[Scripting 索引](README.md) · [Compiler](Inno.Scripting.Compiler.md) · [Extensibility](../extensibility/Inno.Extensibility.Modules.md) · [目标 reload 强制标准](../architecture/IDENTITY_REFERENCE_RELOAD_STANDARD.md)

## 公开 API

- `ScriptReloadHost`：把成功编译结果准备成 Module/Type/feature candidates；Plugin availability 改变而无法产出替代编译时，准备显式的 module retirement candidate；两者都只在安全点原子激活。
- `ScriptReloadOptions`：reload boundary 和诊断策略。
- `IScriptReloadCoordinator`：Editor/Scene/Rendering 等生产 feature 的事务协调 contract。

Reload 不重新编译源文件。普通 Project 编译失败没有 candidate，active generation 保持不变；但 active Plugin 被删除、结构失效或更新失败时，安装集合变化本身是有效事实，不能因为依赖脚本编译失败而保留旧 Plugin。Host 会以 active/candidate Plugin ID 与 content hash 计算 retired Plugin modules，再沿 `upstreamModuleNames` 取得完整反向依赖闭包，原子移除 Plugin、Runtime Scripts 和 Editor Scripts。Scene participant 在同一事务内把已退休类型变成保留 Stable ID 与状态的 Missing，并在类型返回时恢复。

Plugin 文件系统删除触发的第一次自动 reload 就必须完成上述切换，不要求用户再次执行 Reload Plugins。成功编译出的每个 replacement request 使用编译快照给出的显式依赖清单；空清单不会重新绑定仍处于 previous generation 的 Plugin。提交后 unload verification 按帧协作式等待 CLR 完成 collectible ALC 回收。只有超时后仍真实可达时才发布唯一的 `INNO-ALC-UNLOAD` Diagnostic；Console 不再额外写入一条内容重复的普通 Error log。超时后当前 Host 必须重启，不能在 Faulted 进程里开始新的 reload 来清除此诊断。

当前 verification 是 Success-or-Exception barrier：旧 ALC 未被 GC 确认回收前
operation 不成功，达到失败阈值后进入 Faulted，并阻止重叠 reload、Play、Build 与 Export。成功 commit、rollback
candidate、Plugin uninstall、显式 unload 和 shutdown 都受同一规则约束。完整要求见
[Identity、可恢复引用与热重载强制标准](../architecture/IDENTITY_REFERENCE_RELOAD_STANDARD.md)。

只有 participant Prepare/Activate、Scene 状态迁移或外部 generation 同步本身失败时，事务才 rollback 到仍完整存在的 immutable previous snapshot。Complete 清理失败不能撤销已经提交的 generation，必须 Fault 共享 gate；不能只记录诊断后继续。调用者负责 Dispose host，释放 catalog registrations 与 retiring generation lease。

`IScriptReloadCoordinator.Execute(AssemblyReloadSession, IGenerationChange? externalChange = null)` 用统一五阶段 owner 替代两段无所有权 callback。Prepare 在 gate 内执行；参与者 Capture 失败也会回滚已尝试的 prepare，不对未准备的 participant 执行恢复。外部内容 Apply 在 Editor domain 恢复前完成，rollback 先恢复旧 assembly/catalog 与外部内容，再恢复 Editor 属性。ScriptReloadHost 使用 PluginEnvironment.CreateReloadChange，不在末尾另做非原子的 CommitPending。

关闭 ScriptReloadHost 时先撤销 observation、取消并等待编译，然后完成共享 GenerationCoordinator 的上一批 GC barrier，才提交活动模块卸载。退出不是绕过 AwaitingCollection 的特殊入口；Faulted/timeout 仍会阻止新的卸载事务。

自动编译只排除本 ScriptReloadHost 同步发布候选时产生的 SourceMountsChanged 通知回声。Assembly Catalog 会在每次脚本发布时重新发布隔离 Asset Catalog，这不是一次新的源文件编辑，不能由此排队下一轮 reload。真实文件变化、显式编译请求和外部 source-mount 发布仍然正常入队；不关闭 watcher、不取消全局通知，也不跳过 GC barrier。
