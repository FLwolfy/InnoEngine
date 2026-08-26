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
AssetManager.Save("Scenes/Level.iscene", asset);
```

文件名是 Scene 名称权威来源。保存为 `Scenes/Level.iscene` 或外部重命名到该路径后，实例名为 `Level`，而不是 source payload 中的旧名称。

## PrefabAsset

| API | 说明 |
| --- | --- |
| `new PrefabAsset()` | 空资产。 |
| `Capture(GameObject)` | 捕获 root 及完整 child subtree。 |
| `CaptureFrom(GameObject)` | 更新现有 prefab source state。 |
| `Instantiate(GameScene, Transform?)` | 创建新 identity subtree，并可指定 parent。 |

Prefab root 名称同样跟随 `.iprefab` 文件名；child 名称保持捕获值。普通 connected instance 的 name/property override 不会因为 source path 改名而被误覆盖。

## Importer

| 扩展名 | importer ID | 资产 |
| --- | --- | --- |
| `.iscene` | `inno.engine.scene` | `SceneAsset` |
| `.iprefab` | `inno.engine.prefab` | `PrefabAsset` |

Importer 使用统一 async writer，输出 `runtime`，Loader 自动追加 `asset-state`，并把 Scene graph 中的 `AssetObject` 引用登记为 runtime dependencies。

Game Layers 不属于本项目。它由 [Inno.Editor.Settings](../editor/Inno.Editor.Settings.md) 以 `Project/Layers/Game Layers` 路径存入项目根 `EditorSettings.json`，不经过 AssetManager 或 Source Database。

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

## 局部 Scene 状态 API

以下 API 为 Editor History、Prefab 工具和其他需要保留 Scene identity 的 host feature 提供最小粒度数据操作：

| API | 说明 |
| --- | --- |
| `ScenePropertySerialization.CaptureProperty` | 捕获一个 Scene object 的一个 root serialized property。 |
| `ScenePropertySerialization.CaptureProperties` | 捕获一个 Component/System 的全部 persistent properties，不包含 owning Scene。 |
| `ScenePropertySerialization.RestoreProperties` | 使用 Scene reference context 恢复 property-data bytes，支持 Strict/Compatible。 |
| `SceneSubtreeSerialization.Capture` | 捕获一个 GameObject 与全部 descendants，保留对象/组件 persistent ID。 |
| `SceneSubtreeSerialization.Restore` | 把 subtree 恢复到指定 Scene、parent 与 sibling index；失败时清理候选 subtree。 |
| `SceneElementSerialization.RestoreComponent` | 根据 `TypeRef` 与 persistent ID 重建一个 Component，不调用 Reset。 |
| `SceneElementSerialization.RestoreSystem` | 根据 `TypeRef` 与 persistent ID 重建一个 GameSystem，不调用 Reset。 |

这些 API 不保存 Editor selection、Undo 栈或 workspace；该编排属于 [Inno.Editor.Scene](../editor/Inno.Editor.Scene.md)。Element restore 会先解析当前 TypeCache generation 的类型，验证具体基类与 multiplicity。Property restore 只有在 `success=true` 且 `ignoredCount=0` 时才视为完整；失败或忽略属性都会删除新实例。清理返回 `false` 而对象仍存活、返回 `true` 但 postcondition 仍显示对象存活，或清理回调抛异常时，API 会把恢复失败与清理失败一并报告，不再忽略清理结果。

## 多实例约束

Reload 会使用候选类型重新验证数量：

- 多个同类型 Component 但新类型缺少 `[AllowMultipleComponent]`：拒绝整个 reload；
- 多个同类型 System 但新类型缺少 `[AllowMultipleSystem]`：拒绝整个 reload。

系统不会自动删除“多出来”的实例，因为无法可靠判断应保留哪一个及其引用/serialized state。旧代际和 Scene 结构保持活动，用户修复类型声明或删除重复实例后再编译。

## Serialized member compatibility

Scene migration 按成员独立捕获状态。候选类型中名称相同且可解码的成员正常恢复；新增成员保留新默认值；删除成员忽略旧数据；同名成员类型不兼容时只跳过该成员，保留新实例的字段初始化值。

跳过项以 `INNOHR0001` warning 写入 `diagnostics`，包含 Scene/Object persistent ID、成员名、旧类型和新声明类型。`INNOHR0002` 不再属于 Migration 的历史事件列表，而由 Editor 在 loaded Scene 实例集合、TypeCache generation 或 Recompile/Reload 安全点变化时，按当前 Missing 状态完整对账；因此新加载 Scene 自带 Missing 时会在下一次 Editor 主线程安全更新发布，无变化 Recompile 也会重新推送当前诊断，类型恢复后立即从 Console report 中解除。`INNOHR0004` 表示已提交后的旧实例 Detach 清理异常；该清理按实例隔离且不会伪回滚已发布 generation。实例创建、Stable Type ID 冲突、数量约束、Scene 结构或对象级 restore hook 错误仍回滚整个 reload。

候选 generation 缺少 live Component/System 时不再拒绝 reload。Migration 原位换成 `MissingGameComponent` / `MissingGameSystem`，保留 `TypeRef`、persistent ID、显示顺序、中立属性 bytes、Asset dependencies 和引用别名；落盘仍只写原逻辑 Stable ID，不写 placeholder 类型 ID 或 missing 标志。普通 Scene 中 persistent/source token 相同的恒等 alias 会被省略，因此 live → missing → recovered 的规范化序列化保持一致，不会制造 Editor dirty。Scene 和 Prefab 都使用同一 current-format schema，因此 missing 状态可继续保存、实例化和再次保存；只有 Prefab source-local ID 与 Scene runtime persistent ID 确实不同时，引用别名表才写入必要映射，把 payload 中的旧 token 重绑到当前图。

## 生命周期迁移

Edit Mode reload 不调用 `Awake`、`Start`、`OnEnable`、`OnDisable`、`OnDestroy` 或 `Reset`。Runtime reload 对旧 active instance 调用一次 `OnDisable`，新 instance 继承 Awake/Start flags，并在下一次正常 Scene update 调用 `OnEnable`。Coroutine owner 会在旧实例退休前停止。
