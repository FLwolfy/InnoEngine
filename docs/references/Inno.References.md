# Inno.References

[References 索引](README.md) · [Wiki 首页](../README.md) · [专项强制标准](../architecture/IDENTITY_REFERENCE_RELOAD_STANDARD.md)

## 职责与边界

该 Content 层程序集统一持久引用意图、Missing 槽位和批量恢复，不拥有 Assets/Scene/Editor，也不扫描插件程序集。
公开依赖为 Core.Identity 与 Extensibility.Reload；Core.Execution 只用于内部退休控制。
Foundation 的 Settings/Graphs 不得反向引用本项目，相关恢复应由上层 owner 组合。

## 公开协议

| API | 稳定语义 |
| --- | --- |
| `ReferenceKindId` | 开放的解析协议 ID，不是对象 Identity |
| `ReferenceKey(ownerPersistentId, path)` | 一个持久 owner 的稳定槽位；重复 key 在事务捕获时失败 |
| `ReferenceDescriptor(kindId, targetPersistentId, expectedStableTypeId, lastKnownName, lastKnownPath)` | 持久对象引用；名称/路径仅用于诊断，空 target 是 Unassigned |
| `ReferenceResolutionState` | Unassigned / Resolved / Missing / TypeMismatch / Invalid |
| `ReferenceResolution` | descriptor、state、可选 runtimeIdentity、diagnostic；只有 Resolved 携带当前 domain 的 runtimeIdentity |
| `IReferenceResolver.kindId / Resolve` | 当前代解析边界，不发布对象；不得替换传入 descriptor |
| `ReferenceCatalog.empty / Create / Resolve / generation` | immutable resolver 集合，拒绝重复 kind；未安装 resolver 保留 Missing |
| `SerializedMissingState(key, descriptor, payload)` | 中立恢复槽位及复制的 bytes；也用于捕获即将变成 Missing 的 live 槽位 |
| `ReferenceRecoveryChange.missingState / resolution` | provisional 对象应用后的槽位解析结果，不持有目标实例 |
| `IReferenceRecoveryParticipant` | 继承统一 `IGenerationChange` 五阶段协议，并实现 commit 前的 `Validate(changes)` |
| `ReferenceRecoveryTransaction` | 将中立槽位和有序 participant 纳入同一 generation 事务；无自行 Activate/Dispose 的旁路 |

不存在可继承的 Recovery 基类。领域通过接口组合，不必继承与其生命周期无关的类。

## 初始化、发布与回滚

构造 `ReferenceRecoveryTransaction(catalog, missingStates, participants)` 只捕获并检查 key，不改变 live state。
调用 owner 必须在捕获线程和安全点使用同一个 GenerationCoordinator：

1. `PrepareForActivation`：旧 publication 仍有效，逐个登记并 quiesce participant。
2. publication 激活 Assembly/Type/Serializer 与外部 Asset 候选。
3. `Apply`：所有 participant 构建 provisional 对象与属性，然后统一解析 slots、调用所有 `Validate`。
4. publication commit 后调用 `Complete`；退休完成才释放 participant 与 resolver 引用。
5. 提交前失败：逆序 `RollbackStructure` → publication rollback → 逆序 `RestorePreviousState`。

不能在 Apply 的 catch 或 Dispose 中自行恢复旧属性，因为旧 converter/resolver 当时尚未发布。
原子性由整个 generation owner 保证：provisional 状态只允许本次事务访问，不能插入游戏帧或对外报告成功。

```csharp
using System.Collections.Generic;
using Inno.Extensibility.Reload;
using Inno.References;

static TProbe Recover<TProbe>(
    GenerationCoordinator generations,
    IGenerationPublication<TProbe> publication,
    ReferenceCatalog candidate,
    IEnumerable<SerializedMissingState> states,
    IEnumerable<IReferenceRecoveryParticipant> participants)
    where TProbe : IAssemblyUnloadProbe
{
    var recovery = new ReferenceRecoveryTransaction(candidate, states, participants);
    return generations.Execute("reference recovery", publication, [recovery]);
}
```

方法返回 monitor 不代表 collectible ALC 已完成回收；最外层 owner 仍须推进同一 GC barrier。
不要在仍持有 candidate/旧 Type 的调用栈里同步等待自己造成的保留。

## 失败与所有权

- Prepare 开始前登记 participant；部分失败的 Prepare/Apply 也会得到补偿，未开始的 participant 不执行领域回滚。
- Validate 可以允许嵌套 Missing，但不能把 Missing 改写成 null 或伪造 Resolved。
- 构造、阶段顺序、跨线程调用、重复槽位明确失败。
- 普通补偿/清理失败继续尝试其他 owner，聚合上报；generation gate 决定 Fault，不只记录日志。
- RetirementPendingException（包括 Aggregate/InnerException 包装）不归类为普通失败，不执行更低层清理，不清空 participant；保留原始外层错误，共享 gate Fault 后需重启 Host。
- 成功与完整回滚释放 generation owner；`changes` 在成功后可保留用于诊断，回滚后为空。
- `changes` 中的 RuntimeIdentity 是瞬时解析结果，不得写入 History、Settings、Scene 或 Asset metadata。
- `ReferenceCatalog` 冻结的是 resolver 集合；resolver 本身按其明确 owner/generation 读取当前 canonical identity。不能把旧 catalog 当成跨代 live object 缓存。

## 生产接入

SceneReloadService 已用真实 Component/System 和 Asset dependency 槽位创建该事务：
element 槽位使用对象 persistent ID，Stable Type ID 只表达类型约束；Asset 槽位沿用 AssetReferenceProtocol。
Scene domain participant 保留原结构和中立 bytes；类型缺失产生占位，恢复失败按上述两阶段顺序撤销。

Editor History 使用 SceneElementSerialization 的中立 element state，能在类型缺失时 Undo 创建原 ID 的占位；
恢复后原 Redo 可继续使用。History 不建立第二个 Recovery owner，不保存 candidate 或插件对象。
Assets 的全量 importer/canonical recovery、Graph、Settings、Plugin availability 的统一批次接入仍未完成，
不能把这条 Scene/Asset-reference/History 生产链写成整个 C07 已关闭。

RuntimeSession 的 catalog 由 Composition 注入 authoring resolver；Player AssetDatabase 自动贡献 Asset resolver。
领域从明确构造边界接收解析器，通用 RuntimeSubsystemContext 不暴露整个 Session。
引用感知序列化统一使用 owner 的 AssetSerializationContext，不从 SerializationContext.empty 临时拼接。

## 中立内容读取

`ContentReadScope(contents, activeContent)` 捕获 Identity 数组，不保留 Scene、Asset 或其他 live root。
`contents` 是冻结副本；`GetValues<T>()` / `TryGetValue<T>(persistentId, out value)` 通过原弱 Identity registry
解析当前 runtime slot。重复、未注册 identity 和不在集合内的 activeContent 被拒绝。
Dispose 或跨线程读取失败；不会自动重绑定到新 generation。

SceneContentSource.CreateScope(world) 为 Rendering 与 Audio 提供同一个内容协议。
完整未完成项见[累积收口报告](../architecture/ENGINE_CLOSURE_CONTINUATION_2026_09_07.md)。
