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

## Editor Scene 名称与资产路径

已保存 Scene 的 Inspector 名称可以编辑。名称变化立即使文档进入 dirty 状态；保存时同目录 SceneAsset 被事务式重命名，`.imeta`、persistent ID、canonical instance 和 artifact identity 保持不变。目标文件已存在时保存会给出冲突错误，不覆盖另一个资产。
