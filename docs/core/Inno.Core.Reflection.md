# Inno.Core.Reflection

[上一页：Assemblies](Inno.Core.Assemblies.md) · [Core 索引](README.md) · [下一页：Serialization](Inno.Core.Serialization.md)

`Inno.Core.Reflection` 把当前活动 Assembly catalog 转换成不可变的类型快照，并提供统一查询、持久/运行时类型身份和可事务刷新的 `TypeRegistry<TSnapshot>`。所有面向使用者的类型查询都位于公开的 `TypeCacheManager`；旧的 `TypeCache` facade 已移除。

## 初始化关系

```csharp
AssemblyManager.Initialize(new AssemblyManagerOptions { cacheDirectory = cachePath });
TypeCacheManager.Initialize();

// Query types or initialize registries.

TypeCacheManager.Shutdown();
AssemblyManager.Shutdown();
```

Reflection 引用 Assemblies 并注册一个 catalog participant。Assemblies 不引用 Reflection，也不存在 `InternalsVisibleTo` 耦合。

## TypeCacheManager

| 成员 | 说明 |
| --- | --- |
| `bool isInitialized` | TypeCache participant 已注册且 AssemblyManager 仍有效。 |
| `TypeCacheSnapshot current` | 当前不可变快照；读取前会先处理 dirty Host catalog。 |
| `Initialize()` | 注册类型发现与所有 TypeRegistry 的统一事务参与者。 |
| `Rebuild()` | 通过 AssemblyManager 强制重建 assembly、type 与 registry 快照。 |
| `Shutdown()` | 注销 participant，释放 Registry 状态并恢复空快照。 |
| `GetSubTypesOf<T>()` | 返回所有具体派生类型。 |
| `GetTypesImplementing<TInterface>()` | 返回所有具体接口实现。 |
| `GetTypesWithAttribute<TAttribute>()` | 返回所有带指定 attribute 的具体类型。 |
| `GetTypeRef(Type)` | 把当前 CLR Type 转换为不持有 ALC 的 `TypeRef`。 |
| `TryGetTypeRef(Type, out TypeRef)` | 安全尝试同一转换。 |

查询结果只包含非抽象、非接口的匹配实现，并按照 catalog 的稳定顺序输出。

```csharp
IReadOnlyList<TypeRef> behaviors = TypeCacheManager.GetSubTypesOf<GameBehavior>();
IReadOnlyList<TypeRef> converters =
    TypeCacheManager.GetTypesWithAttribute<SerializationExtensionAttribute>();

TypeRef player = TypeCacheManager.GetTypeRef(typeof(PlayerController));
Console.WriteLine(player.Resolve().FullName);
```

## TypeRef

`TypeRef` 是公开 readonly value type，只含 `Guid stableId` 与 `int runtimeId`。公开构造器只接收 Stable ID；TypeCache 生成的值才带 runtime hint。`isValid` 始终针对当前快照，`Resolve()` 无法解析时抛 `InvalidOperationException`，`Resolve(snapshot)` 可在事务中分别解析 previous/candidate。

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

Runtime Type ID 是只适用于某个 CLR `Type` 实例的整数。新 ALC 里的替代 Type 会获得新的 runtime ID；失败/回滚候选已分配的 ID 也不会复用。它只在 `TypeIdentityRegistry` 和 TypeCache query index 内部用于快速验证/定位当前 CLR Type，并作为 `TypeRef.Resolve` 的可选 hint；Scene、Asset、History、Workspace、Editor action 等领域存储和索引都直接保存 `TypeRef`，不消费裸 runtime ID。

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

这是 Importer、Serialization Converter、Inspector Drawer 等扩展点的通用基类。它在候选 TypeCache 上构建完整不可变索引，与 TypeCache 一起原子 activate/rollback。

| 成员 | 可见性 | 说明 |
| --- | --- | --- |
| 构造函数 | `protected` | 自动弱注册到 TypeRegistry 协调器。 |
| `isInitialized` | `public` | 是否已有活动快照。 |
| `Refresh()` | `public` | 从当前 TypeCache 主动刷新；相同版本不会重复构建。若 TypeCache 在激活回调期间变化，完成当前 transaction 后再以独立 transaction 追平。 |
| `Clear()` | `public` | 释放快照但保留 Registry，可在下次访问重建。 |
| `Dispose()` | `public` | 注销并释放，之后不能再用。 |
| `current` | `protected` | 懒取得当前快照；版本过期时自动刷新。 |
| `Build(TypeCacheSnapshot)` | `protected abstract` | 旁路验证并构建完整候选快照。 |
| `OnActivating(previous, candidate)` | `protected virtual` | candidate 已临时发布后的可失败激活；实现必须准备对应回滚。 |
| `OnActivationRolledBack(previous, candidate)` | `protected virtual` | 激活失败后逆转已完成的生命周期工作。 |
| `OnActivationCompleted(previous, current)` | `protected virtual` | 全部 Registry 激活成功后的不可失败清理阶段。 |
| `DisposeSnapshot(snapshot)` | `protected virtual` | 默认对实现 `IDisposable` 的 snapshot 调用 `Dispose()`。 |
| `OnCleanupFailed(phase, exception)` | `protected virtual` | 报告 rollback/complete/snapshot release 清理异常；不会重新进入回滚。 |
| `CreateExtension<TExtension>(Type)` | `protected static` | 验证具体类型并通过无参构造函数实例化。 |

### 自定义 Registry 示例

```csharp
internal sealed record RenderPipelineSnapshot(
    IReadOnlyDictionary<string, RenderPipeline> pipelines);

internal sealed class RenderPipelineRegistry
    : TypeRegistry<RenderPipelineSnapshot>
{
    public bool TryGet(string id, out RenderPipeline? pipeline)
        => current.pipelines.TryGetValue(id, out pipeline);

    protected override RenderPipelineSnapshot Build(TypeCacheSnapshot types)
    {
        Dictionary<string, RenderPipeline> result = new(StringComparer.Ordinal);
        foreach (TypeRef typeRef in types.GetSubTypesOf<RenderPipeline>())
        {
            Type type = typeRef.Resolve(types);
            RenderPipeline instance = CreateExtension<RenderPipeline>(type);
            if (!result.TryAdd(instance.id, instance))
                throw new InvalidOperationException($"Duplicate pipeline id '{instance.id}'.");
        }

        return new RenderPipelineSnapshot(result);
    }
}
```

新增这种 Registry 不需要修改 `AssemblyManager` 或添加全局 Hook。协调器执行 Build candidate → reversible Activate → global Complete；后一个 Registry 激活失败时，已激活项按逆序恢复 `m_current` 与 TypeCache version，candidate 被释放，previous snapshot 保留。Complete 只做 previous release 等清理，异常会逐项报告并继续，不会制造“已发布后伪回滚”。完成后不会再执行 pending Registry refresh；重入排队的 rebuild 必须在外层全局 transaction 完成后作为独立 transaction 运行，失败只回滚自己的 candidate。候选构造/冲突验证失败同样保留当前 Registry。

## 错误模型

`TypeCacheBuildException` 表示枚举程序集类型时发生一个或多个 loader error：

- `loaderExceptions`：每个底层异常。
- `InnerException`：第一个 loader exception（若存在）。

TypeCache 不再静默吞掉 `ReflectionTypeLoadException`。这对热重载很重要：缺依赖的候选不能部分进入全局查询。

## 热重载注意事项

- 查询永远只看当前活动 generation；旧 ALC 即使尚未被 GC，也不会重新出现在 TypeCache。
- 同一程序集名与完整类型名通常保持 Stable ID，但运行时 `Type` 对象不是同一个。
- Registry snapshot 不应跨代际缓存旧 `Type`、delegate 或 extension instance。
- `TypeCacheManager.Rebuild()` 适合“活动程序集集合未重新载入，但需要重算类型/Registry”的场景；读取新 DLL 仍由 Assemblies 的 Reload API 完成。
