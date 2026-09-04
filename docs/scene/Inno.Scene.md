# Inno.Scene

[Engine 索引](README.md) · [Scene Assets](Inno.Scene.Assets.md) · [Wiki 首页](../README.md)

`Inno.Scene` 提供 Scene、GameObject、Component、Transform hierarchy、GameBehavior 和 GameSystem 的运行时模型。`GameBehavior` 是唯一具有独立启停和帧生命周期的 Component 基类，统一负责 `enabled`、`isActiveAndEnabled`、`Awake`、`Start`、`OnEnable`、`OnDisable`、`Update`、`FixedUpdate`、`LateUpdate` 与 `OnDestroy`。Project Script、Renderer、Camera、Light 等场景功能都直接继承 `GameBehavior`，不存在第二层 `Behavior` 类型或兼容 façade。

`Transform` 除了便捷的 local/world TRS 外，还公开精确的 `localToWorldMatrix`、`worldToLocalMatrix`、`TransformPoint`、`InverseTransformPoint` 与原子 `SetWorldTransform`。`worldPosition` 与点变换统一由完整层级矩阵确定。旋转且非均匀缩放的多级层级可能包含不能由单一 world TRS 无损表示的 shear；渲染、包围盒和空间查询应优先使用矩阵/点变换 API，Inspector 与 Gizmo 的 TRS 编辑则使用原子 setter 避免三次中间重算。零缩放层级不可逆，世界到本地转换和保持世界值的重设父级会明确失败，不会静默使用 identity inverse。

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

Name 与 Tag 都按 `StringComparison.Ordinal` 匹配，复数查询保持 Scene 创建顺序。内部 `SceneStore` 除 Name/Tag/Layer 索引外还维护私有 Unique + Ordered 顺序键；删除对象导致 dense storage swap-back 时不会改变其余对象的查询顺序。查询通过标准 `Query().Find(...).OrderBy(...).Get()/First()` 契约执行，不依赖 Scene 专用 Storage API；对象元数据变化时只更新对应 entry。Tag 会随 Scene、Prefab 和 prefab override 一起序列化。

可用 Tag 定义由 `GameTagCatalog` 这个普通 Project Setting 提供；assignment 与 definition 明确分离：

```csharp
GameTagCatalog tags = ProjectSettingsStore.Get<GameTagCatalog>(GameTagCatalog.settingId);
if (tags.IsDefined("Player"))
    player.tag = "Player";
```

`GetTags` 返回确定性隔离快照；`Add`/`Remove` 只修改当前可编辑 setting 实例。删除定义不会改写 Scene 中已有字符串，因此重新定义同名 Tag 可恢复其配置语义。

## GameLayer、GameLayerMask 与 GameLayerStack

`Inno.Scene.Layers` 将对象所属层、筛选集合与项目配置明确分开：

| 类型 | 职责 |
| --- | --- |
| 类型 | 职责 |
| --- | --- |
| `GameLayerId` | 当前 `ProjectId` 与 layer local key 组合出的 `projectId.name` 身份。 |
| `GameLayer` | 0–31 的紧凑运行时 slot；Scene/Prefab 只保存这个值。 |
| `GameLayerMask` | 32 位多层集合，用于渲染、物理和查询过滤。 |
| `GameLayerDefinition` | 自动 local key、当前 slot 与显示名称的只读快照。 |
| `GameLayerStack` | 最多 32 个自动 local key/name 映射及对称 interaction matrix。 |

用户只编辑 Layer 名称和 slot，不输入 ID。新 slot 自动得到稳定 local key（例如 `layer.01`）；完整 ID 只在需要时由当前 Project ID 解析。Project ID 改名不会触碰 Layer setting、Scene 或 Prefab；Layer 显示名改名也不会改变已经生成的 local key。

```csharp
using InnoEngine.Scene;
using InnoEngine.Settings;

GameLayerStack layers = Settings.Get<GameLayerStack>(GameLayerStack.settingId);
GameLayer player = layers.GetLayer("Player");
GameLayer enemy = layers.GetLayer("Enemy");
GameLayerId playerId = layers.GetId(Settings.projectId, player)!.Value;
GameLayerMask visible = layers.GetMask(["Player", "Enemy"]);

gameObject.layer = player;
layers.SetInteraction(player, enemy, canInteract: false);
```

`GameLayer.defaultLayer` 固定为 slot 0、local key `default` 和名称 `Default`。其完整 ID 会随项目身份解析为 `projectId.default`。Plugin contribution 同样只携带 local key/slot/name 和 interaction operation，不携带导出项目的 Project ID；导入后自然落在消费项目命名空间下。

Composer 对相同 local key、slot、name 的声明去重；同一 slot/key 或 interaction pair 的不兼容声明仍要求显式依赖与 override。32 个 slot 是 Layer mask 的真实有限资源，Composer 不会重排 slot 或改写 Scene assignment。

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

## GameSystem 定位

`GameSystem` 是附加到 `GameScene` 而不是单个 `GameObject` 的有状态对象。它适合表达“每个 Scene 一份、需要序列化、需要 Inspector 配置、并参与 Scene 生命周期”的协调逻辑，例如物理世界、导航世界、寻路网格实例、场景级音频环境、昼夜控制、波次导演、实体索引或面向某种渲染模型的 Scene extraction cache。默认每个具体类型在一个 Scene 中只允许一个实例；只有显式标记 `AllowMultipleSystem` 的类型才能重复。

`GameSystem` 不等于所有引擎 service 的通用基类。图形设备、RenderGraph、Player loop、AssetDatabase、编译器和 Editor service 的 owner 都高于或独立于单个 Scene，把它们继承 `GameSystem` 会让后端生命周期被 Scene 加载状态绑死，并制造 Rendering → Scene 的反向依赖。当前 Rendering Runtime 因此使用 host-owned frame boundary 和 attribute 驱动的 `RenderRequestProvider`；`Inno.Rendering.Scene` 只把 `SceneWorld` 投影成中立 `RenderContentScope`。具体渲染 Plugin 可以在确实需要持久化 Scene 级配置或增量 extraction 状态时提供自己的 `GameSystem`，但普通 Camera、Renderer 与 Light 仍应是直接附着到对象的 `GameBehavior`。

生命周期调度缓存 loaded Scene、GameBehavior 与 GameSystem 的稳定数组，只在 Scene 结构、System 显示顺序或类型 generation 变化时重建。两种类型的 `enabled` 变化都会在 loaded Scene 中立即协调 `OnEnable`/`OnDisable`，不等待下一帧。`GameSystem.order` 仍会在每个阶段开始前读取；值变化时复用现有数组原地排序。`GameSystem.Query<T...>()` 内部使用最多三个规范排序的当前 generation runtime type ID 组成值类型 key，缓存命中时也不再创建 `Type[]`、规范化数组或字符串 key。因此正常 FixedUpdate、Update 与 LateUpdate 不再为这些 traversal 分配新数组。

`SceneTypeCatalog` 在 candidate generation 构建时一次性判断每个 `GameBehavior` 是否实际 override
Awake/Start/Enable/Disable/Update/Fixed/Late/Destroy，并把结果压缩为 lifecycle phase mask。
Runner 按 mask 维护 activation、一次性 Start、Update、FixedUpdate 与 LateUpdate 的独立索引。
Awake/Enable/Disable 通过结构或 enabled/hierarchy 变化事件进入一次性同步队列，Start 成功后立即退出
启动队列；没有覆盖帧 callback 的 Renderer 不会进入对应逐帧数组。结构 replacement/removal 会立即
清空旧索引引用，普通结构 revision 或类型 generation 变化才重建索引。因此仍然只有唯一
`GameBehavior` 基类，不需要用第二个 Renderer/Behavior 层级换取性能或牺牲 ALC 卸载。

Scene 级增量 extraction cache 直接继承 `GameSystem`。其 protected `GetObjects()` 返回由 Scene
持有的不可变结构快照；对象或 Component attachment 变化会使快照 identity 和 structure revision
一起失效，普通 Transform/材质/颜色数值变化不会触发全量重新索引。具体 Rendering Plugin 可据此
缓存 Camera/Drawable/Light 引用，在每帧读取当前值；设备、RenderGraph 和 Runtime 仍不属于
`GameSystem`，不会产生 Rendering Core → Scene 的反向依赖。

## 内部索引与类型身份

以下是当前内部实现细节，不属于额外公开 API：

- GameObject、GameComponent 与 GameSystem 都以 `IndexedObjectStore<T>` 保存；引用身份、persistent Guid、元数据、owner、commit 状态和 runtime type ID 分别使用 typed `IndexedObjectKey<TKey>`。
- GameObject 与 GameComponent 的 persistent Guid 使用 Unique key，因此 `FindObject(Guid)` 与 `FindComponent(Guid)` 为平均 O(1) 查找，不再递归或线性扫描 Scene。
- Component/System 的具体类型索引、Entry、查询缓存和组合查询 key 全部保存当前 TypeCache generation 的 `int runtimeId`。可赋值关系由类型目录预计算为 runtime ID 集合，查询期间不调用 `Type.IsAssignableFrom`，也不在 Scene 中保存 CLR `Type`。
- 每个对象的 Component list 仅维护公开契约要求的 attachment order；它不承担对象身份、Guid 或类型索引职责。System 的 display list 同理只维护显示与序列化顺序。

Scene/Prefab 的 History、Missing、序列化和 reload previous/candidate 边界继续使用 `TypeRef`，序列化只写其 Stable ID（Guid），绝不写入 runtime ID。Scene 的当前代内存索引只保存 runtime ID，并在 generation 切换时由 migration 使用候选或 previous `TypeRef.runtimeId` 原地替换或回滚。`Type` 参数只存在于 Add/Query、实例创建和序列化反射等调用边界的短生命周期局部变量中；Component 构造器与 Scene property metadata 不建立静态 `Type` 缓存。

## Missing 脚本元素

`GameBehavior` 与 `GameSystem` 分别使用 Core Scripting 的 `ScriptingAttachableTypeAttribute` 声明自己的脚本 manifest 类别。Editor Scripting 只读取该中立 metadata，不硬编码或引用 Scene 类型；具体实例迁移和 Missing 行为仍完全由 Scene 领域拥有。

| 公开类型 | 说明 |
| --- | --- |
| `MissingGameComponent` | 原 Component 类型暂时不可用时，占据相同 attachment index 和 persistent ID。公开 `TypeRef missingType`、`missingTypeName` 供 Inspector/工具识别。 |
| `MissingGameSystem` | 原 System 类型暂时不可用时，占据相同 display index 和 persistent ID，并保持禁用。公开同样的 missing 类型信息。 |

这两个类型只能由 Scene restore 或脚本 reload 管线创建，不能通过普通 `AddComponent` / `AddSystem` 添加，也不进入 Scripting API facade。占位对象只保存 `TypeRef`、类型名、中立 property bytes、资产依赖 token 和引用别名；`TypeRef` 不保存旧 `Type`、反射 metadata、委托或旧脚本实例，所以本身不会阻止 collectible ALC 卸载。原 Stable ID 再次可解析时，`missingType.isValid` 自动变回 true，reload 原位创建真实类型、恢复兼容属性和当前图引用；构造、属性或清理失败则连同 identity/index 一起回滚到完全相同的占位对象。

Missing 是运行时占位状态，不是 Scene 数据格式中的额外元素类型或 dirty 修改。序列化仍写原逻辑 Stable Type ID、原类型名和原 property bytes，不写 `MissingGameComponent` / `MissingGameSystem` 的 Stable ID，也不写 missing 标志；普通 Scene 中恒等的引用 token 不产生冗余 alias。因而 clean Scene 在类型消失或恢复时保持 clean，Hierarchy 不显示 `*`。用户可以在 missing 存在时修改并保存其他内容；后续相同 Stable ID 恢复时，保存过的原始状态仍会原位还原。

## 热重载同步

Scene 使用一个随 `TypeRegistry<TSnapshot>` 事务刷新的中立类型目录。候选目录构建时可以读取 candidate `TypeCacheSnapshot`，但发布后的目录不保留任何 `Type` 或 `TypeRef`：Component/System descriptor 保存 runtime type ID、可赋值 runtime ID 集合与 multiplicity 标志；Store、Scheduler 与查询缓存直接以 `int` 为 key。

Reload 的同步顺序如下：

1. Capture 阶段把旧实例属性编码为不含 `Type` 的中立 bytes，并记录 previous runtime type ID、Stable Type ID、资产依赖和图引用命名空间。
2. TypeCache 与中立 Scene 类型目录原子激活 candidate，同时清除所有存活 SceneStore 的类型派生数组缓存。
3. Migration 有 replacement 时创建新实例；没有 replacement 时创建 host-owned missing 占位；已存在的占位在 Stable Type ID 恢复时创建真实实例。三种路径都以 candidate runtime type ID 原地更新 Component/System typed key。
4. 成功时立即释放旧实例图和序列化迁移快照；失败时先用捕获的 previous runtime type ID 恢复索引，再回滚 TypeCache 和类型目录。

这样旧 Scene 索引或引擎内部数组不会因为持有 collectible ALC 的 `Type` 而阻止卸载。调用方如果自行长期保留旧 `TypeCacheSnapshot`、旧 Component/System 实例或先前返回的强引用快照，仍会按 .NET 规则延长旧 ALC 生命周期，调用方应在 reload safe point 释放它们。

## Editor Scene 名称与资产路径

已保存 Scene 的 Inspector 名称可以编辑。名称变化立即使文档进入 dirty 状态；保存时同目录 SceneAsset 被事务式重命名，`.imeta`、persistent ID、canonical instance 和 artifact identity 保持不变。目标文件已存在时保存会给出冲突错误，不覆盖另一个资产。
