# Inno.Core.Storage

[上一页：Mathematics](Inno.Core.Mathematics.md) · [Core 索引](README.md) · [前往 Assets](../assets/README.md)

Storage 提供两个独立的数据结构：线程安全、方向明确的 `DependencyGraph<TKey>`，以及支持多索引与查询优化的 `IndexedObjectStore<T>`。

## DependencyGraph&lt;TKey&gt;

边 `node -> dependency` 表示“node 依赖 dependency”。Graph 接受环；只有要求 DAG 的操作会显式报告环。

### 构造与状态

```csharp
DependencyGraph<string> graph = new(
    equalityComparer: StringComparer.Ordinal,
    orderingComparer: StringComparer.Ordinal);
```

- `count`：节点数。
- `version`：成功结构变更后递增。
- 未提供 ordering comparer 时，查询使用节点插入顺序保证确定性。

### 修改 API

| 方法 | 说明 |
| --- | --- |
| `ContainsNode(node)` | 节点是否存在。 |
| `AddNode(node)` | 新增节点，存在时 false。 |
| `RemoveNode(node)` | 删除节点及所有入/出边。 |
| `AddDependency(node, dependency)` | 自动创建两端节点并添加边。 |
| `RemoveDependency(node, dependency)` | 移除边。 |
| `ReplaceDependencies(node, IEnumerable<TKey>)` | 原子替换全部直接依赖。 |
| `Clear()` | 清除全部节点和边。 |

### 查询 API

| 方法 | 说明 |
| --- | --- |
| `GetDependencies(node, recursive=false)` | 直接或传递依赖。 |
| `GetDependents(node, recursive=false)` | 直接或传递反向依赖。 |
| `DependsOn(node, dependency, recursive=false)` | 判断依赖关系。 |
| `TopologicalSort()` | dependency 在 dependent 之前；有环时抛异常。 |
| `TryFindCycle(out cycle)` | 找到一条确定性的环路径。 |
| `GetStronglyConnectedComponents()` | 返回强连通分量。 |

```csharp
graph.AddDependency("Scene", "Texture");
graph.AddDependency("Material", "Texture");
IReadOnlyList<string> order = graph.TopologicalSort();
```

当前共享消费者包括 Asset runtime/import dependency、Assembly reload module、Plugin manifest、Script asmdef、Project Settings contributor、通用 Graph validation 和 ShaderGraph node。它们统一使用相同的边方向、确定性排序、环路径与反向依赖定义，不再各自维护 DFS/Kahn 变体。

RenderGraph 编译器和 Job scheduler 内部没有改用该容器：前者每帧处理紧凑整数索引并需要资源 hazard 来源诊断，后者直接维护可复用 job slot/dependent counter；在这些高频路径套用带读写锁和通用 key 的持久 Graph 会增加分配与锁竞争。它们只共享算法语义，不共享存储表示。

## IndexedObjectStore&lt;T&gt;

Store 按引用身份存储 class，支持为同一对象定义多个 typed key。名称使用 `Store` 是为了明确表达其职责：它不负责对象创建、复用或生命周期回收，而是“稠密对象集合 + 多索引 + 查询”容器。

### Store API

| 方法/属性 | 说明 |
| --- | --- |
| `count` | 当前对象数。 |
| `DefineKey<TKey>(name, flags, comparer?)` | 创建 typed index，返回 `IndexedObjectKey<TKey>`。 |
| `RemoveKey(key)` | 删除 index；旧 handle 失效。 |
| `TryGetKey<TKey>(name, out key)` | 名称和 TKey 都匹配时返回 handle。 |
| `GetAllKeys()` | lazy fail-fast key name enumerable。 |
| `Add(item)` | 按引用去重添加，返回用于链式 Set 的 `IndexedObjectEntry<T>`。 |
| `Remove(item)` | 删除对象及所有 key 值。 |
| `FindFast(key,value)` | lazy fail-fast 匹配结果。 |
| `Find(key,value)` | 与后续修改隔离的快照。 |
| `First(key,value)` | 首个匹配或 null。 |
| `All()` / `AllFast()` | 全量快照 / lazy fail-fast。 |
| `Query()` | 新建 `IndexedObjectQuery<T>` builder。 |
| `RemoveAll()` | 清对象但保留 keys。 |
| `Clear()` | 清对象和 keys；所有 key handle 失效。 |

### Key flags 与 handle

`IndexedObjectKeyFlags` 是 flags：

- `Unordered`：hash lookup，不维护 key 顺序。
- `Ordered`：使用提供的 comparer 维护顺序；Define 时 comparer 必填。
- `Unique`：每个 key value 最多一个 item。

`IndexedObjectKey<TKey>` 公开 `name` 与 `isValid`。它通过对 owning store 的弱引用验证，不会延长 store 生命周期。`IndexedObjectEntry<T>.isValid` 表示 item 仍在 store；`Set(key,value)` 更新索引并返回自身以便链式调用。

```csharp
IndexedObjectStore<Entity> store = new();
IndexedObjectKey<Guid> byId = store.DefineKey<Guid>("id", IndexedObjectKeyFlags.Unique);
IndexedObjectKey<int> byOrder = store.DefineKey<int>(
    "order",
    IndexedObjectKeyFlags.Ordered,
    Comparer<int>.Default);

store.Add(entity)
    .Set(byId, entity.id)
    .Set(byOrder, entity.order);

Entity? found = store.First(byId, entity.id);
```

## IndexedObjectQuery&lt;T&gt;

| API | 说明 |
| --- | --- |
| `Where(IIndexedObjectQueryCondition<T>)` | 添加自定义 condition。 |
| `Where(Func<T,bool>)` | 包装为 `IndexedObjectPredicateCondition<T>`。 |
| `Find(key,value)` | 添加 `IndexedObjectKeyCondition` 条件。 |
| `OrderBy(orderedKey)` | 按已声明 Ordered 的 key 输出。 |
| `GetFast()` | lazy fail-fast enumerable。 |
| `Get()` | 稳定快照。 |
| `First()` | 首个匹配或 null。 |

普通与 Ordered executor 都会优先选择估计候选数最小的 indexed condition，再验证其他 condition。Ordered 查询在存在可索引条件时只读取候选对象已经维护的 order key，并按 comparer 排序；`First()` 只在候选中寻找最小 order key，不构造完整结果。只有全部条件都是无法提供候选集的 predicate 时才遍历完整有序索引。相同 order key 使用当前 dense storage position 作为确定性 tie-breaker；需要跨删除稳定顺序的调用方应自行维护 Unique + Ordered 顺序键。

## 自定义查询条件

`IIndexedObjectQueryCondition<T>` 需要实现：

- `GetCandidateCount(IndexedObjectStore<T>)`：估计候选数，不知道时返回 `int.MaxValue`。
- `TryGetSingle(...)`：可直接定位唯一结果时返回它。
- `GetSet(...)`：可提供候选集合时返回集合，否则 null。
- `Validate(store,item)`：最终判断。

内置 `IndexedObjectKeyCondition<T,TKey>` 使用 index；`IndexedObjectPredicateCondition<T>` 全扫描并可从 `Func<T,bool>` 隐式转换。

## 快照与 Fast 枚举

- `Get` / `Find` / `All` 返回分离快照，之后修改 Store 不影响结果。
- `GetFast` / `FindFast` / `AllFast` 避免复制，但枚举期间 Store version 改变会抛异常。
- Unique key 的重复值会抛 `InvalidOperationException`，不会静默覆盖另一个对象。
- 更新已有对象的 Unique key 时会先验证新值；若新值冲突，旧索引仍保持有效。
- Storage-order 查询是通用公开语义，Scene 等使用方不需要 friend assembly 或访问 Store 内部句柄；实现仍由 Store 在读锁内结合索引与 dense insertion index 完成。
