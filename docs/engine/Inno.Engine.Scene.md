# Inno.Engine.Scene

[Engine 索引](README.md) · [Scene Assets](Inno.Engine.Scene.Assets.md) · [Wiki 首页](../README.md)

`Inno.Engine.Scene` 提供 Scene、GameObject、Component、Transform hierarchy、GameBehavior 和 GameSystem 的运行时模型。

## 多 Scene

```csharp
SceneManager.LoadScene(first);                 // Single: unload current set.
SceneManager.LoadSceneAdditive(second);        // Add to the bottom and make active.
SceneManager.SetSceneIndex(second, 0);         // Change hierarchy/enumeration order.
SceneManager.SetActiveScene(first);             // Does not reorder scenes.
SceneManager.MoveGameObjectToScene(player, second); // Moves the complete subtree.
```

`loadedScenes` 返回 Hierarchy 展示顺序的稳定快照。Editor 双击 SceneAsset 使用 additive open；已经打开的同一路径会被激活和选择，而不会创建重复实例。Ctrl/Cmd+S 保存全部打开的 Scene。

Hierarchy 的 Scene context menu 和 Delete hotkey 会关闭该内存 Scene，但不会删除对应的 SceneAsset。最后一个已加载 Scene 也可以关闭；此时 `SceneManager.activeScene` 为 `null`，Hierarchy 保持为空，直到用户显式创建或打开 Scene。

Scene 顺序决定当前 `SceneManager` 的跨 Scene traversal 顺序，但业务脚本不应把它作为精确的脚本执行顺序契约；显式依赖应放入可排序的 GameSystem 或独立 scheduler。

`MoveGameObjectToScene` 要求 source 与 destination 都已加载。被移动对象会成为目标 Scene 的 root；完整 child subtree、GameObject/Component 实例、persistent ID、世界变换和生命周期状态保持不变。该操作不会通过序列化复制对象，也不会调用 Reset 或 Destroy。

## Name 与 Tag 查询

每个 `GameObject` 默认使用 `GameObject.defaultTag`（`"Untagged"`），并允许通过普通字符串设置项目 Tag：

```csharp
player.tag = "Player";

GameObject? named = scene.FindObject("Player Root");
GameObject? firstPlayer = scene.FindObjectWithTag("Player");
IReadOnlyList<GameObject> players = scene.FindObjectsWithTag("Player");
```

Name 与 Tag 都按 `StringComparison.Ordinal` 匹配，复数查询保持 Scene storage order。内部 `SceneStore` 通过 `IndexedObjectKey<string>` 持续维护 Name 与 Tag 索引；对象元数据变化时只更新对应 entry，不需要在下一次查询时重扫完整 Scene。Tag 会随 Scene、Prefab 和 prefab override 一起序列化。

## GameLayer、GameLayerMask 与 GameLayerStack

`Inno.Engine.Scene.Layers` 将对象所属层、筛选集合与项目配置明确分开：

| 类型 | 职责 |
| --- | --- |
| `GameLayer` | 0–31 的稳定单层索引。`GameObject.layer` 始终只保存一个值。 |
| `GameLayerMask` | 32 位多层集合，用于渲染、物理和 Scene 查询过滤。 |
| `GameLayerDefinition` | 一个已命名 slot 的只读快照。 |
| `GameLayerStack` | 最多 32 个名称及对称 interaction matrix。 |

层名称不写入 Scene 或 Prefab。对象只序列化数值 slot，因此重命名配置不会重写资产，也不会破坏引用；移除名称后对象仍保留原 slot，重新定义同一 slot 即可恢复显示语义。

```csharp
using Inno.Engine.Scene;
using Inno.Engine.Scene.Layers;

GameLayerStack layers = settings.layerStack;
GameLayer player = layers.GetLayer("Player");
GameLayer enemy = layers.GetLayer("Enemy");
GameLayerMask visible = layers.GetMask(["Player", "Enemy"]);

gameObject.layer = player;
GameObject? firstPlayer = scene.FindObjectWithLayer(player);
IReadOnlyList<GameObject> visibleObjects = scene.FindObjectsWithLayers(visible);

layers.SetInteraction(player, enemy, canInteract: false);
bool canCollide = layers.CanInteract(player, enemy); // false in both directions
```

`GameLayer.defaultLayer` 固定为 slot 0，名称固定为 `Default`，不能删除。自定义名称按 ordinal 规则唯一。`GameLayer` 与 `GameLayerMask` 都有显式 SerializationConverter，可以安全用于 `[SerializableProperty]`。

## Component 顺序

```csharp
GameComponent[] components = [.. gameObject.GetComponents()];
gameObject.SetComponentIndex(components[2], 1);
int index = gameObject.GetComponentIndex(components[2]);
```

- `Transform` 永远保持 index `0`，不能移动。
- 顺序由 Scene/Prefab serialization 保存。
- `GetComponents()` 与 Inspector 使用相同顺序。
- Inspector 通过 header 右侧的上下箭头逐位移动 Component；到达边界的箭头会禁用。
- 手动顺序不改变 GameBehavior Update 优先级；需要确定性调度时使用专门 scheduler，而不是依赖 Inspector 位置。

## GameSystem 顺序

GameSystem 有两个明确分离的排序概念：

| 顺序 | API | 含义 |
| --- | --- | --- |
| 显示/序列化顺序 | `GetSystems`、`GetSystemIndex`、`SetSystemIndex` | Inspector 排列与 Scene round-trip。 |
| 执行优先级 | `GameSystem.order` | 生命周期按数值从小到大执行。 |

```csharp
public sealed class PhysicsSystem : GameSystem
{
    public override int order => -100;
}
```

Inspector header 提供 Move Up、Move Down 和 Remove，但不允许拖拽。移动按钮只改变显示与序列化顺序，不会修改代码声明的 `order`；相同 `order` 时显示顺序作为稳定 tie-breaker。

生命周期调度缓存 loaded Scene、GameBehavior 与 GameSystem 的稳定数组，只在 Scene 结构、System 显示顺序或类型 generation 变化时重建。`GameSystem.order` 仍会在每个阶段开始前读取；值变化时复用现有数组原地排序。`GameSystem.Query<T...>()` 内部使用最多三个按 Stable ID 规范排序的 `TypeRef` 组成值类型 key，缓存命中时也不再创建 `Type[]`、规范化数组或字符串 key。因此正常 FixedUpdate、Update 与 LateUpdate 不再为这些 traversal 分配新数组。

## 内部索引与类型身份

以下是当前内部实现细节，不属于额外公开 API：

- GameObject、GameComponent 与 GameSystem 都以 `IndexedObjectStore<T>` 保存；引用身份、persistent Guid、元数据、owner、commit 状态和 runtime type ID 分别使用 typed `IndexedObjectKey<TKey>`。
- GameObject 与 GameComponent 的 persistent Guid 使用 Unique key，因此 `FindObject(Guid)` 与 `FindComponent(Guid)` 为平均 O(1) 查找，不再递归或线性扫描 Scene。
- Component/System 的具体类型索引、查询缓存和组合查询 key 全部保存 `TypeRef`。可赋值关系由类型目录预计算为 `TypeRef` 集合，查询期间不调用 `Type.IsAssignableFrom`，也不在 Scene 中建立 `Type` 或裸 runtime ID key。
- 每个对象的 Component list 仅维护公开契约要求的 attachment order；它不承担对象身份、Guid 或类型索引职责。System 的 display list 同理只维护显示与序列化顺序。

Scene/Prefab 的内存索引、描述、History、Missing 与序列化边界统一使用 `TypeRef`，序列化只写其 Stable ID（Guid），绝不写入 runtime ID。`Type` 参数只存在于 Add/Query、实例创建和序列化反射等调用边界的短生命周期局部变量中；Component 构造器与 Scene property metadata 不建立静态 `Type` 缓存。旧 generation 创建的 `TypeRef` 仍可凭 Stable ID 与候选目录匹配，不需要在 Scene 切换或重建整数索引。

## Missing 脚本元素

| 公开类型 | 说明 |
| --- | --- |
| `MissingGameComponent` | 原 Component 类型暂时不可用时，占据相同 attachment index 和 persistent ID。公开 `TypeRef missingType`、`missingTypeName` 供 Inspector/工具识别。 |
| `MissingGameSystem` | 原 System 类型暂时不可用时，占据相同 display index 和 persistent ID，并保持禁用。公开同样的 missing 类型信息。 |

这两个类型只能由 Scene restore 或脚本 reload 管线创建，不能通过普通 `AddComponent` / `AddSystem` 添加，也不进入 Scripting API facade。占位对象只保存 `TypeRef`、类型名、中立 property bytes、资产依赖 token 和引用别名；`TypeRef` 不保存旧 `Type`、反射 metadata、委托或旧脚本实例，所以本身不会阻止 collectible ALC 卸载。原 Stable ID 再次可解析时，`missingType.isValid` 自动变回 true，reload 原位创建真实类型、恢复兼容属性和当前图引用；构造、属性或清理失败则连同 identity/index 一起回滚到完全相同的占位对象。

## 热重载同步

Scene 使用一个随 `TypeRegistry<TSnapshot>` 事务刷新的中立类型目录。候选目录构建时可以读取 candidate `TypeCacheSnapshot`，但发布后的目录不保留任何 `Type`：Component/System descriptor 保存 `TypeRef`、可赋值 `TypeRef` 集合与 multiplicity 标志；Store、Scheduler 与查询缓存直接以 `TypeRef` 为 key。

Reload 的同步顺序如下：

1. Capture 阶段把旧实例属性编码为不含 `Type` 的中立 bytes，并记录 previous runtime type ID、Stable Type ID、资产依赖和图引用命名空间。
2. TypeCache 与中立 Scene 类型目录原子激活 candidate，同时清除所有存活 SceneStore 的类型派生数组缓存。
3. Migration 有 replacement 时创建新实例；没有 replacement 时创建 host-owned missing 占位；已存在的占位在 Stable Type ID 恢复时创建真实实例。三种路径都以 candidate runtime type ID 原地更新 Component/System typed key。
4. 成功时立即释放旧实例图和序列化迁移快照；失败时先用捕获的 previous runtime type ID 恢复索引，再回滚 TypeCache 和类型目录。

这样旧 Scene 索引或引擎内部数组不会因为持有 collectible ALC 的 `Type` 而阻止卸载。调用方如果自行长期保留旧 `TypeCacheSnapshot`、旧 Component/System 实例或先前返回的强引用快照，仍会按 .NET 规则延长旧 ALC 生命周期，调用方应在 reload safe point 释放它们。

## Editor Scene 名称与资产路径

已保存 Scene 的 Inspector 名称可以编辑。名称变化立即使文档进入 dirty 状态；保存时同目录 SceneAsset 被事务式重命名，`.imeta`、persistent ID、canonical instance 和 artifact identity 保持不变。目标文件已存在时保存会给出冲突错误，不覆盖另一个资产。
