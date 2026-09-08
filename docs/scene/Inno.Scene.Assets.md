# Inno.Scene.Assets

[Scene 索引](README.md) · [Scene 契约与恢复](Inno.Scene.md) · [Assets Pipeline](../assets/Inno.Assets.Pipeline.md) · [Wiki 首页](../README.md)

## 职责与边界

这是独立的导入程序集，只负责 .iscene / .iprefab 的创作源与 Artifact 转换。
SceneAsset、PrefabAsset、ScenePropertySerialization、SceneElementSerialization、
SceneSubtreeSerialization、SceneReloadService 实际属于 Inno.Scene，namespace 为 Inno.Scene。

本项目的两个 Importer 都是 internal sealed 类型，通过 AssetImporterExtension 自动发现。
当前没有对外公开类型或面向外部派生者的 protected 扩展点；用户扩展 Importer 应面向 Assets.Pipeline，
不能依赖这里的内部实现。项目依赖 Inno.Scene、Inno.Assets、Inno.Assets.Pipeline、Core.Serialization，
不引用 Editor 或具体 backend。

## 初始化与产物

先建立 ModuleHost / TypeCatalog / SerializationRegistry，再让默认 Content 装配包含本程序集，
由 AssetPipeline 的候选 Importer Registry 发现：

| 扩展名 | 稳定 importer ID | 资产契约 |
| --- | --- | --- |
| .iscene | inno.engine.scene | Inno.Scene.SceneAsset |
| .iprefab | inno.engine.prefab | Inno.Scene.PrefabAsset |

导入器读取统一 Serialization 的 EngineResourceEnvelope，验证 resource kind，
登记全部 Asset dependencies，并通过 async writer 输出 runtime artifact。
asset-state 由通用 Loader 追加；本项目不建立自己的数据库、JSON reader 或 schema migration。

## 常见工作流

下面是 Host/Editor 基础设施代码；调用时必须处于 scene 的正确 owner scope，
且 AssetPipeline 已发现本程序集。

```csharp
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.Serialization;
using Inno.Scene;

static bool SaveScene(GameScene scene, SerializationRegistry serialization, AssetPipeline assets)
{
    SceneAsset asset = SceneAsset.Capture(scene, serialization, assets);
    return assets.Save(AssetPath.Project("Scenes/Level.iscene"), asset);
}
```

游戏脚本不负责导入/保存，使用显式 Scripting API。Editor 用户修改走 SceneEdits 和统一 History，
而不是直接从 Panel 调用上述保存流程。

## 错误、Missing 与热重载

- 损坏 envelope、错误 kind、不可用依赖由统一导入事务和诊断报告，失败不能发布半套 Artifact。
- Scene/Prefab 中的 Missing element 仍由 Inno.Scene 保留逻辑 Type ID、对象 ID、原 bytes、依赖和引用别名。
- Scene 名称由 .iscene 文件名确定；source-only rename 经 AssetPipeline 保留 Asset persistent ID。
- 删除打开 Scene 的源文件不等于卸载正在编辑的 Scene；工作区负责保留当前编辑数据。
- 素材恢复必须带回同一 .imeta 身份；同路径的新 Asset 不能接管旧引用。
- Loader、Artifact、Importer generation 继续使用 Assets 的 owner 与候选事务，不在此建立另一个 Recovery owner。

局部元素恢复及 Missing Undo 的完整公开 API 见 [Inno.Scene](Inno.Scene.md)，
Editor 使用方式见 [Inno.Editor.Scene](../editor/Inno.Editor.Scene.md)。
