# Inno.Engine.Scene.Assets

[Engine 索引](README.md) · [Editor Scripting](../editor/Inno.Editor.Scripting.md) · [Wiki 首页](../README.md)

该项目拥有 `SceneAsset`、`PrefabAsset`、对应 Importer、Scene graph 序列化以及脚本对象迁移实现。它与 `Inno.Engine.Scene` 是深度配对模块，仍保留唯一、明确的 friend assembly 边界；Editor 只能通过公开的迁移门面访问它。

## 资产类型

- `SceneAsset.Capture(GameScene)`：把运行时 Scene 捕获为待保存资产。
- `SceneAsset.Instantiate()`：创建未加载的新 Scene。
- `PrefabAsset.Capture(GameObject)`：捕获对象子树。
- `PrefabAsset.Instantiate(GameScene, Transform?)`：以新 identity 实例化到目标 Scene。

## SceneReloadService

```csharp
ISceneReloadMigration migration =
    SceneReloadService.Capture(typeCacheReloadContext);
```

这是 Editor Scripting 使用的最小公开边界。`ISceneReloadMigration` 提供：

- `retiredObjects`
- `PrepareForActivation()`
- `Apply()`
- `RollbackStructure()`
- `RestorePreviousState()`
- `Complete()`

调用顺序必须与程序集事务一致：先 capture/prepare，再 `AssemblyReloadSession.Activate()`，随后 apply/complete；异常时先回滚 Scene 结构，再 rollback 程序集，最后恢复旧生命周期状态。

接口公开是为了消除 `Scene.Assets -> Editor.Scripting` 的 friend assembly，而不是游戏脚本 API。该类型没有出现在项目的 `Properties/ScriptingApi.cs` 中，因此 GameScripts/EditorScripts 都看不到它。

## Reload 与多实例约束

脚本候选代际在 Scene 状态捕获阶段会使用新类型重新检查 Component/System 数量：

- 同一 GameObject 上，同一候选 Component 类型存在多个实例，但新类型没有 `[AllowMultipleComponent]` 时，整个 reload 被拒绝。
- 同一 Scene 中，同一候选 System 类型存在多个实例，但新类型没有 `[AllowMultipleSystem]` 时，整个 reload 被拒绝。
- 旧脚本代际、Scene 对象与序列化状态保持不变；错误会包含 Scene/GameObject identity、类型和重复数量。

这里不会自动删除“多出来”的实例，因为无法可靠判断哪一个实例及其引用、序列化数据应该保留。用户应先删除重复实例，或恢复相应 allow-multiple attribute，再触发下一次编译。
