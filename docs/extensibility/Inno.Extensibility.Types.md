# Inno.Extensibility.Types

[上一页：Modules](Inno.Extensibility.Modules.md) · [Extensibility 索引](README.md) · [Serialization](../core/Inno.Core.Serialization.md)

## 组合资源退休

TypeRegistry 新增 protected `ReportRetirementFailure(Exception)`：供派生 registry 报告由该代际创建、但在领域组合中持有的资源退休失败。它将共享 gate 置为 Faulted，并调用诊断 hook；不是吞异常的 fallback。调用方仍须向上传播清理失败。

Catalog participant 在同一个 publication 中重入 Rebuild 时合并为后续对账；不允许借此越过其他事务、读租约或 GC barrier。

Registry 现在通过 Core `LifetimeScope` / `RetirementBarrier` 逆序退休实例。普通错误聚合；Pending 先排空，
不能跳到下一项释放其依赖。达到 deadline 时保留失败 batch/snapshot/transaction，Fault 共享 gate；
重复 Clear/Dispose/Refresh 仍抛同一未退休失败，不清空字段伪装成功。这些强引用仅属于 Faulted Host 的
未完成退休所有权，不是可持久化状态或下一 generation 的缓存。
Pending/timeout 在 Aggregate 或普通 InnerException 中也相同处理，统一使用 Core 分类。
Registry、TypeCatalog、ModuleHost 保留原始外层异常与 owner，而非仅保存抽出的 Pending 子异常。

`Inno.Extensibility.Types` 把当前活动 Assembly catalog 转换成不可变的类型快照，并提供统一查询、持久/运行时类型身份和可事务刷新的 `TypeRegistry<TSnapshot>`。所有面向使用者的类型查询都位于公开的 `TypeCatalog`；旧的 `TypeCache` facade 已移除。

## 初始化关系

```csharp
using var modules = new ModuleHost(new ModuleHostOptions { cacheDirectory = cachePath });
using var types = new TypeCatalog(modules);
types.Rebuild();
// Registries must retire before types and modules.
```

Reflection 引用 Assemblies 并注册一个 catalog participant。Assemblies 不引用 Reflection，也不存在 `InternalsVisibleTo` 耦合。

## TypeCatalog

| 成员 | 说明 |
| --- | --- |
| `bool isInitialized` | TypeCache participant 已注册且 ModuleHost 仍有效。 |
| `TypeCacheSnapshot current` | 当前不可变快照；读取前会先处理 dirty Host catalog。 |
| `TypeCatalog(ModuleHost)` | 创建实例并注册类型/Registry 统一事务参与者。 |
| `Rebuild()` | 通过 ModuleHost 强制重建 assembly、type 与 registry 快照。 |
| `AcquireOperation(string)` | 同步领域操作期间保护捕获的 generation，延后自动刷新；发布线程内部可借用，其他发布并发访问拒绝。 |
| `Dispose()` | 注销 participant 并释放 Registry 状态。 |
| `GetSubTypesOf<T>()` | 返回所有具体派生类型。 |
| `GetTypesImplementing<TInterface>()` | 返回所有具体接口实现。 |
| `GetTypesWithAttribute<TAttribute>()` | 返回所有带指定 attribute 的具体类型。 |
| `GetTypeRef(Type)` | 把当前 CLR Type 转换为不持有 ALC 的 `TypeRef`。 |
| `TryGetTypeRef(Type, out TypeRef)` | 安全尝试同一转换。 |
| `TryResolve(TypeRef, out Type?)` / `Resolve(TypeRef)` | 对当前 generation 解析；前者返回可用性，后者在缺失时抛异常。 |

查询结果只包含非抽象、非接口的匹配实现，并按照 catalog 的稳定顺序输出。

跨多次查询或修改捕获的领域 owner 时，应持有 `AcquireOperation` 到整个操作和补偿结束。
它直接复用 ModuleHost 的 GenerationCoordinator，不触发对账；需要最新 Catalog 时先读取 `current`，
再捕获 owner 和建立 scope。旧 ALC 等待回收时允许读取已发布数据，不因此放开 Play/Build/Export；
异步 Build/Export 仍使用严格的 `GenerationCoordinator.AcquireRead` 并持有到消费者退出。

```csharp
IReadOnlyList<TypeRef> behaviors = types.GetSubTypesOf<GameBehavior>();
IReadOnlyList<TypeRef> converters =
    types.GetTypesWithAttribute<SerializationExtensionAttribute>();

TypeRef player = types.GetTypeRef(typeof(PlayerController));
Console.WriteLine(player.Resolve(types).FullName);
```

## TypeRef

`TypeRef` 是公开 readonly value type，只含 `Guid stableId` 与 `int runtimeId`。公开构造器只接收 Stable ID；TypeCache 生成的值才带 runtime hint。`IsValid(TypeCatalog)` 针对指定 catalog，`Resolve(TypeCatalog)` 无法解析时抛 `InvalidOperationException`，`Resolve(snapshot)` 可在事务中分别解析 previous/candidate。

相等性和 HashCode 只看 `stableId`，所以同一逻辑类型跨 generation 仍相等；`runtimeId` 只是进程内不复用的快速查找 hint。解析会验证 runtime 命中的 Stable ID，hint 过期或命中不符时回退到 Stable ID。`default(TypeRef)` 与空 Guid 无效。统一 Serialization converter 只写 `stableId`，绝不持久化 `runtimeId`/`isValid`。

`TypeRef` 不保存 `Type`、`Assembly`、delegate 或 ALC，也没有进入 Scripting API export。长期集合可以安全保存它；外部保存 `Resolve()` 返回的 CLR `Type`/对象/委托仍会按 .NET 规则延迟旧 ALC 卸载。

## TypeCacheSnapshot

快照可用于一次多查询需要严格一致版本的场景：

| 成员 | 说明 |
| --- | --- |
| `long version` | 单调递增版本号。 |
| `IReadOnlyList<TypeRef> types` | 本 generation 的全部已发现类型身份。 |
| `GetSubTypesOf<T>()` | 快照内的具体派生类。 |
| `GetTypesImplementing<TInterface>()` | 快照内的具体接口实现。 |
| `GetTypesWithAttribute<TAttribute>()` | 快照内带 attribute 的类型。 |
| `GetTypeRef(Type)` / `TryGetTypeRef` | 把属于该快照的 CLR Type 转成 `TypeRef`。 |

不要长期缓存旧 snapshot：其内部为了 generation 一致性强持有 `Type` 和反射发现 slice，即使公开查询只返回 `TypeRef`。Registry 在 `Complete/Rollback` 中及时释放旧快照；外部调用方若自行保留旧 snapshot 或 `Resolve` 的结果，则 ALC 延迟卸载属于该引用的预期结果。

Snapshot 构建会按 `Assembly` 引用身份复用上一代的内部 Type slice：未变化的 host/default/upstream assembly 不再重复调用 `GetTypes()`，新加载或新 ALC 中的 assembly 才重新发现。该优化不会跨 ALC 复用脚本 `Type`；即使程序集字节来自增量缓存，被替换的 Plugin/Runtime/Editor ALC 中 `Assembly` 引用也不同，因此旧 slice 会随 previous snapshot 一起释放。Snapshot 本身必须保持强引用才能提供 generation 内一致性；把其中的 `Type` 改成弱引用会让一次查询中类型集合随 GC 变化，不能解决外部强引用，反而破坏事务语义。

## Stable Type ID 与 Runtime Type ID

普通 Host/Plugin 类型在没有 attribute 时，Stable ID 由 `程序集简单名 + 完整类型名` 生成确定性 UUIDv5。脚本编译器可以通过 assembly metadata 提供 source-based canonical ID；TypeCache 只验证并消费当前映射，不依赖 Asset/Scripting 项目。类型级 `[StableTypeId]` 始终优先于编译器映射。

需要重命名兼容时显式固定：

```csharp
[StableTypeId("c5db9123-9768-4e34-a346-22981ee4b4da")]
public sealed class PlayerController : GameBehavior
{
}
```

`StableTypeIdAttribute.id` 是 Guid 字符串。无效字符串或重复 ID 会让候选快照验证失败，从而保留旧 generation。

`GetTypeRef` 返回唯一 canonical ID；`TypeRef.Resolve` 只接受当前明确注册的 ID，不维护 former alias 或旧持久数据兼容表。

Runtime Type ID 是只适用于某个 CLR `Type` 实例的整数。新 ALC 里的替代 Type 会获得新的 runtime ID；失败/回滚候选已分配的 ID 也不会复用。它在 `TypeIdentityRegistry`、TypeCache query index，以及明确绑定当前 TypeCache generation 的 Scene Component/System 内存索引与查询缓存中使用。`TypeRef.Resolve` 也把它作为可选快速 hint。Asset、History、Workspace、Editor action、Missing、序列化和 reload 边界继续保存 `TypeRef`，不得让裸 runtime ID 跨 generation 或持久化。

## TypeCacheReloadContext

程序集 Reload session 可取得这个上下文，用来迁移旧实例：

| 成员 | 说明 |
| --- | --- |
| `previous` | 旧类型快照。 |
| `candidate` | 已验证的候选类型快照。 |
| `IsRetired(TypeRef)` | 逻辑类型是否在候选中被替换或移除。 |
| `TryResolveReplacement(TypeRef, out TypeRef)` | 按 Stable ID 查找不同的候选 generation identity。 |

上下文只在 reload transaction 存活；`Complete()` 或 `Rollback()` 后访问会抛 `InvalidOperationException`。

## TypeRegistry&lt;TSnapshot&gt;

清理失败不再只是日志。Complete 会尝试全部旧 snapshot 清理，然后抛出聚合异常并把所属
GenerationCoordinator 置为 Faulted；已提交 candidate 不伪回滚，Host 必须重启。
Rollback 同样尝试全部已准备项，任何恢复/释放失败都不得报告成功。Build 过程中创建的资源在验证失败时统一逆序释放；成功后由 snapshot 接管，不能在派生 Build catch 中重复释放。
OnCleanupFailed 仅用于 override 观测，
不能吞掉真实失败或批准继续 generation 操作。

这是 Importer、Serialization Converter、Inspector Drawer 等扩展点的通用基类。它在候选 TypeCache 上构建完整不可变索引，与 TypeCache 一起原子 activate/rollback。

| 成员 | 可见性 | 说明 |
| --- | --- | --- |
| `TypeRegistry(TypeCatalog, TimeSpan? retirementTimeout = null)` | `protected` | 自动弱注册；退休 deadline 为正值，默认 30 秒，在首次退出尝试时绑定 owner thread。 |
| `isInitialized` | `public` | 是否已有活动快照。 |
| `Refresh()` | `public` | 从当前 TypeCache 主动刷新；相同版本不会重复构建。若 TypeCache 在激活回调期间变化，完成当前 transaction 后再以独立 transaction 追平。 |
| `Clear()` | `public` | 释放快照但保留 Registry，可在下次访问重建。 |
| `Dispose()` | `public` | 注销并释放，之后不能再用。 |
| `current` | `protected` | 懒取得当前快照；版本过期时自动刷新。 |
| `Build(TypeCacheSnapshot)` | `protected abstract` | 旁路验证并构建完整候选快照。 |
| `OnActivating(previous, candidate)` | `protected virtual` | candidate 已临时发布后的可失败激活；实现必须准备对应回滚。 |
| `OnActivationRolledBack(previous, candidate)` | `protected virtual` | 激活失败后逆转已完成的生命周期工作。 |
| `OnActivationCompleted(previous, current)` | `protected virtual` | 全部 Registry 激活成功后的 cleanup-only 阶段；失败必须报告 Fault，不能再进行发布。 |
| `DisposeSnapshot(snapshot)` | `protected virtual` | 默认对实现 `IDisposable` 的 snapshot 调用 `Dispose()`。 |
| `OnCleanupFailed(phase, exception)` | `protected virtual` | 报告 rollback/complete/snapshot release 清理异常；不会重新进入回滚。 |
| `CreateExtension<TExtension>(Type)` | `protected` | 验证并创建实例，自动登记 candidate rollback ownership。 |
| `OwnCandidateExtension<T>(T)` | `protected` | 将自定义工厂创建的新实例纳入 Build 失败补偿；不能传入借用的旧实例。 |
| `DisposeExtensions(IEnumerable<object>)` | `protected` | 按引用去重后交由 LifetimeScope 逆序退休；Pending 保留本项及以下依赖，普通失败仍尝试后续项。 |
| `ReportRetirementFailure(Exception)` | `protected` | 组合资源无法退休时封锁共享 generation，并通知诊断。 |
| `RetireResource(string owner, Action retire)` | `protected` | 在 control-thread safe point 有界排空单个可重试 Stop/Detach；正常完成不保存 delegate，超时保留未完成 owner 并 Fault。不是实时 callback API。 |

### 自定义 Registry 示例

```csharp
using System;
using System.Collections.Generic;
using System.Collections.Frozen;
using Inno.Extensibility.Types;

internal abstract class ExampleExtension : IDisposable
{
    public abstract string id { get; }
    public abstract void Dispose();
}

internal sealed class ExampleRegistry(TypeCatalog types)
    : TypeRegistry<FrozenDictionary<string, ExampleExtension>>(types)
{
    protected override FrozenDictionary<string, ExampleExtension> Build(TypeCacheSnapshot snapshot)
    {
        var entries = new Dictionary<string, ExampleExtension>(StringComparer.Ordinal);
        foreach (TypeRef typeRef in snapshot.GetSubTypesOf<ExampleExtension>())
        {
            ExampleExtension entry = CreateExtension<ExampleExtension>(typeRef.Resolve(snapshot));
            if (!entries.TryAdd(entry.id, entry))
                throw new InvalidOperationException($"Duplicate extension ID: {entry.id}");
        }
        return entries.ToFrozenDictionary(StringComparer.Ordinal);
    }

    protected override void DisposeSnapshot(FrozenDictionary<string, ExampleExtension> snapshot)
        => DisposeExtensions(snapshot.Values);
}
```

新增这种 Registry 不需要修改 `ModuleHost` 或添加全局 Hook。协调器执行 Build candidate → reversible Activate → global Complete；后一个 Registry 激活失败时，已激活项按逆序恢复 `m_current` 与 TypeCache version，candidate 被释放，previous snapshot 保留。Complete 只做 previous release 等清理，异常会逐项报告并继续，不会制造“已发布后伪回滚”。完成后不会再执行 pending Registry refresh；重入排队的 rebuild 必须在外层全局 transaction 完成后作为独立 transaction 运行，失败只回滚自己的 candidate。候选构造/冲突验证失败同样保留当前 Registry。

## 错误模型

`TypeCacheBuildException` 表示枚举程序集类型时发生一个或多个 loader error：

- `loaderExceptions`：每个底层异常。
- `InnerException`：第一个 loader exception（若存在）。

TypeCache 不再静默吞掉 `ReflectionTypeLoadException`。这对热重载很重要：缺依赖的候选不能部分进入全局查询。

## 热重载注意事项

- 查询永远只看当前活动 generation；旧 ALC 即使尚未被 GC，也不会重新出现在 TypeCache。
- 同一程序集名与完整类型名通常保持 Stable ID，但运行时 `Type` 对象不是同一个。
- Registry snapshot 不应跨代际缓存旧 `Type`、delegate 或 extension instance。
- `TypeCatalog.Rebuild()` 适合“活动程序集集合未重新载入，但需要重算类型/Registry”的场景；读取新 DLL 仍由 Assemblies 的 Reload API 完成。
