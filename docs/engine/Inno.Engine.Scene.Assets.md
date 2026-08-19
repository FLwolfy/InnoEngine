# Inno.Engine.Scene.Assets

[Engine 索引](README.md) · [Assets](../assets/README.md) · [Editor Scripting](../editor/Inno.Editor.Scripting.md) · [Wiki 首页](../README.md)

该项目拥有 `SceneAsset`、`PrefabAsset`、对应 Importer，以及 Scene/Prefab graph serialization、override processing 和脚本对象 reload migration。Serialization namespace 统一为 `Inno.Engine.Scene.Assets`；它不是独立程序集。

## SceneAsset

| API | 说明 |
| --- | --- |
| `new SceneAsset()` | 为反序列化或后续 `CaptureFrom` 建立空资产。 |
| `Capture(GameScene)` | 捕获 Scene 并返回待保存资产。 |
| `CaptureFrom(GameScene)` | 更新现有 SceneAsset 的 pending source state。 |
| `Instantiate()` | 创建 identity 全新的、尚未加载的 `GameScene`。 |

```csharp
var scene = new GameScene("Level");
scene.CreateObject("Player");
SceneAsset asset = SceneAsset.Capture(scene);
AssetManager.Save("Scenes/Level.innoscene", asset);
```

文件名是 Scene 名称权威来源。保存为 `Scenes/Level.innoscene` 或外部重命名到该路径后，实例名为 `Level`，而不是 source payload 中的旧名称。

## PrefabAsset

| API | 说明 |
| --- | --- |
| `new PrefabAsset()` | 空资产。 |
| `Capture(GameObject)` | 捕获 root 及完整 child subtree。 |
| `CaptureFrom(GameObject)` | 更新现有 prefab source state。 |
| `Instantiate(GameScene, Transform?)` | 创建新 identity subtree，并可指定 parent。 |

Prefab root 名称同样跟随 `.innoprefab` 文件名；child 名称保持捕获值。普通 connected instance 的 name/property override 不会因为 source path 改名而被误覆盖。

## Importer 与数据兼容

| 扩展名 | importer ID | 资产 |
| --- | --- | --- |
| `.innoscene` | `inno.engine.scene` | `SceneAsset` |
| `.innoprefab` | `inno.engine.prefab` | `PrefabAsset` |

Importer 使用统一 async writer，输出 `runtime`，Loader 自动追加 `asset-state`，并把 Scene graph 中的 `AssetObject` 引用登记为 runtime dependencies。Stable Type ID、扩展名和现有序列化 schema 保持兼容。

## 外部 rename/delete

- source-only rename 保留 Scene/Prefab asset persistent ID，`.imeta` 跟随移动，CAS key 不因路径变化而移动。
- loaded `SceneAsset`/`PrefabAsset` canonical instance 的 `sourcePath` 原位更新。
- clean Editor Scene 改名后仍 clean；dirty Scene 保留 dirty flag。
- 删除当前打开 Scene source 不会销毁正在编辑的 `GameScene`，document 进入 missing/dirty 并可在新路径保存。
- FileBrowser selection 与当前目录根据 committed `AssetChangeSet` 更新，而不是直接响应 watcher thread。

## SceneReloadService

```csharp
ISceneReloadMigration migration =
    SceneReloadService.Capture(typeCacheReloadContext);
```

`ISceneReloadMigration` 提供 `retiredObjects`、`diagnostics`、`PrepareForActivation`、`Apply`、`RollbackStructure`、`RestorePreviousState`、`Complete`。Editor Scripting 的顺序是：

1. capture/prepare Scene state；
2. activate Assembly/TypeCache/Registry candidate；
3. apply new Component/System instances；
4. complete Scene 与 Assembly transactions；
5. 异常时反向 rollback 并恢复旧生命周期状态。

该 migration API 是 host boundary，不在 Scripting API facade 中导出。

## 多实例约束

Reload 会使用候选类型重新验证数量：

- 多个同类型 Component 但新类型缺少 `[AllowMultipleComponent]`：拒绝整个 reload；
- 多个同类型 System 但新类型缺少 `[AllowMultipleSystem]`：拒绝整个 reload。

系统不会自动删除“多出来”的实例，因为无法可靠判断应保留哪一个及其引用/serialized state。旧代际和 Scene 结构保持活动，用户修复类型声明或删除重复实例后再编译。

## Serialized member compatibility

Scene migration 按成员独立捕获状态。候选类型中名称相同且可解码的成员正常恢复；新增成员保留新默认值；删除成员忽略旧数据；同名成员类型不兼容时只跳过该成员，保留新实例的字段初始化值。

跳过项以 `INNOHR0001` warning 写入 `diagnostics`，包含 Scene/Object persistent ID、成员名、旧声明类型和新声明类型。Editor 只在程序集事务成功提交后输出这些 warning。实例创建、Stable Type ID、数量约束、Scene 结构或对象级 restore hook 错误仍回滚整个 reload。

## 生命周期迁移

Edit Mode reload 不调用 `Awake`、`Start`、`OnEnable`、`OnDisable`、`OnDestroy` 或 `Reset`。Runtime reload 对旧 active instance 调用一次 `OnDisable`，新 instance 继承 Awake/Start flags，并在下一次正常 Scene update 调用 `OnEnable`。Coroutine owner 会在旧实例退休前停止。
