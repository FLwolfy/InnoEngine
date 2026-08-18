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
| `TryGetStableTypeId(Type, out Guid)` | 获取可跨 generation/进程持久化的 ID。 |
| `TryGetRuntimeTypeId(Type, out int)` | 获取当前运行代际内的快速 ID。 |
| `TryResolveType(Guid, out Type?)` | 由 Stable ID 找当前活动 Type。 |
| `TryResolveType(int, out Type?)` | 由当前 generation 的 Runtime ID 找 Type。 |

查询结果只包含非抽象、非接口的匹配实现，并按照 catalog 的稳定顺序输出。

```csharp
IReadOnlyList<Type> behaviors = TypeCacheManager.GetSubTypesOf<GameBehavior>();
IReadOnlyList<Type> converters =
    TypeCacheManager.GetTypesWithAttribute<SerializationExtensionAttribute>();

if (TypeCacheManager.TryGetStableTypeId(typeof(PlayerController), out Guid id) &&
    TypeCacheManager.TryResolveType(id, out Type? activeType))
{
    Console.WriteLine(activeType.FullName);
}
```

## TypeCacheSnapshot

快照可用于一次多查询需要严格一致版本的场景：

| 成员 | 说明 |
| --- | --- |
| `long version` | 单调递增版本号。 |
| `IReadOnlyList<Type> types` | 本 generation 的全部已发现类型。 |
| `GetSubTypesOf<T>()` | 快照内的具体派生类。 |
| `GetTypesImplementing<TInterface>()` | 快照内的具体接口实现。 |
| `GetTypesWithAttribute<TAttribute>()` | 快照内带 attribute 的类型。 |
| `TryGetStableTypeId` / `TryGetRuntimeTypeId` | 查询类型身份。 |
| `TryResolveType(Guid/int, ...)` | 从 ID 解析快照中的类型。 |

不要长期缓存旧 snapshot：其 `Type` 会保留对应 collectible ALC。Registry 应在 `Complete/Rollback` 中及时释放旧快照。

## Stable Type ID 与 Runtime Type ID

没有 attribute 时，Stable ID 由 `程序集简单名 + 完整类型名` 生成确定性 UUIDv5。相同逻辑类型跨 generation 保持一致；重命名类型或程序集会改变自动 ID。

需要重命名兼容时显式固定：

```csharp
[StableTypeId("c5db9123-9768-4e34-a346-22981ee4b4da")]
public sealed class PlayerController : GameBehavior
{
}
```

`StableTypeIdAttribute.id` 是 Guid 字符串。无效字符串或重复 ID 会让候选快照验证失败，从而保留旧 generation。

Runtime Type ID 是只适用于当前活动 `Type` 实例的整数。新 ALC 里的替代 Type 会获得新的 runtime ID，不能写入持久化数据。

## TypeCacheReloadContext

程序集 Reload session 可取得这个上下文，用来迁移旧实例：

| 成员 | 说明 |
| --- | --- |
| `previous` | 旧类型快照。 |
| `candidate` | 已验证的候选类型快照。 |
| `IsRetiredType(Type)` | 类型是否在候选中被替换或移除。 |
| `TryResolveReplacement(Type, out Type?)` | 按 Stable ID 查找不同的候选 Type。 |

上下文只在 reload transaction 存活；`Complete()` 或 `Rollback()` 后访问会抛 `InvalidOperationException`。

## TypeRegistry&lt;TSnapshot&gt;

这是 Importer、Serialization Converter、Inspector Drawer 等扩展点的通用基类。它在候选 TypeCache 上构建完整不可变索引，与 TypeCache 一起原子 activate/rollback。

| 成员 | 可见性 | 说明 |
| --- | --- | --- |
| 构造函数 | `protected` | 自动弱注册到 TypeRegistry 协调器。 |
| `isInitialized` | `public` | 是否已有活动快照。 |
| `Refresh()` | `public` | 从当前 TypeCache 主动刷新；相同版本不会重复构建。 |
| `Clear()` | `public` | 释放快照但保留 Registry，可在下次访问重建。 |
| `Dispose()` | `public` | 注销并释放，之后不能再用。 |
| `current` | `protected` | 懒取得当前快照；版本过期时自动刷新。 |
| `Build(TypeCacheSnapshot)` | `protected abstract` | 旁路验证并构建完整候选快照。 |
| `OnCommitted(previous, current)` | `protected virtual` | 新状态提交后、旧状态释放前执行。 |
| `DisposeSnapshot(snapshot)` | `protected virtual` | 默认对实现 `IDisposable` 的 snapshot 调用 `Dispose()`。 |
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
        foreach (Type type in types.GetSubTypesOf<RenderPipeline>())
        {
            RenderPipeline instance = CreateExtension<RenderPipeline>(type);
            if (!result.TryAdd(instance.id, instance))
                throw new InvalidOperationException($"Duplicate pipeline id '{instance.id}'.");
        }

        return new RenderPipelineSnapshot(result);
    }
}
```

新增这种 Registry 不需要修改 `AssemblyManager` 或添加全局 Hook。候选构造/冲突验证失败会使整个程序集候选失效，当前 Registry 继续可用。

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
