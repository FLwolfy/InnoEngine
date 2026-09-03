# Inno.Assets.Pipeline

[Assets 索引](README.md) · [Runtime Assets](Inno.Assets.md) · [Plugins](../plugins/Inno.Plugins.Authoring.md)

## 职责与边界

该 authoring project 拥有 Source Mount、文件索引/watcher、Importer、Build Processor、`.imeta`、依赖图、Catalog、CAS Artifact、canonical authoring object 和 runtime closure export。Player 不引用它。

## 公开 API

| 分组 | 主要 API |
| --- | --- |
| Composition | `AssetPipeline`, `AssetPipelineOptions`, `AssetPipelineMode`, `AssetSourcePolicy`, `AssetCacheOptions` |
| Source | `AssetSourceMount`, `AssetSourceMountTransaction`, `AssetFileSystem`, `AssetFileEntry`, `AssetSample`, `AssetChangedEvent` |
| Import | `AssetImporter`, `AssetImporter<T>`, `AssetImporterExtensionAttribute`, `AssetImportContext`, `AssetImportWriter<T>`, `AssetImportHealthSnapshot`, `AssetImportFailure` |
| Build | `AssetBuildProcessor`, `AssetBuildProcessor<T>`, `AssetBuildProcessorExtensionAttribute`, `AssetBuildContext<T>`, `AssetArtifactWriter` |
| Transactions/export | `AssetCatalogCandidate`, `AssetExportContext`, `AssetSerializationServices`, `AssetDeploymentScope`, `NativeAssetSourceSerialization` |
| Script authoring | `EditorAssets` 提供当前 Editor Session 中显式受限的保存入口；不暴露 Pipeline owner 或 mutation graph。 |
| Advanced facade | `AssetLoader`，用于独立 authoring host；普通 Application 优先使用 `AssetPipeline` |

Plugin Importer 使用 `InnoEditor.Assets` 中的 Attribute、Importer base、Context、Writer 与
`NativeAssetSourceSerialization`。原生结构化 Source helper 只接收 Context 提供的
`AssetSerializationServices`，不会向 Plugin 泄漏 `TypeCatalog`、`SerializationRegistry` 或候选
Asset resolver。Importer 源码属于 Plugin 的 Editor assembly，不能进入 Runtime Plugin assembly。

## 组合与生命周期

```csharp
using var assets = new AssetPipeline(
    modules,
    types,
    serialization,
    identities,
    diagnostics,
    logs,
    AssetPipelineOptions.Create(assetRoot, libraryRoot));

assets.Update();
TextAsset value = assets.Load<TextAsset>(AssetPath.Project("Config/value.txt"));
```

所有 mutation 必须在构造线程执行。Save、Import、ImportSample、Move、Delete、CreateDirectory 和 source candidate commit 各自发布一个 revision；后台 `ExportRuntimeArtifactsAsync` 只使用 owner thread 捕获的 immutable Serialization generation。

## `.isample` authoring-only 目录

任意 Source Mount 中，名称以 `~` 开头的目录都是逻辑 `.isample`。`AssetFileSystem` 仍索引目录及后代，`AssetFileEntry.isSample` 标记该目录本身，`isSampleContent` 标记完整子树；因此 Editor 可以浏览它们。Asset Import/Catalog、Artifact、Runtime export 与脚本编译不会处理该子树，删除 `Library` 后重建也保持相同行为。

`AssetPipeline.ImportSample(source)` 把选中的 `.isample` 原子复制到 Project `Assets` 根，并只从所选根目录名移除前导 `~`。复制保留 `.imeta`，所以 Sample 内部 persistent reference 不会因导入断裂；`.abin` 与 source noise 不复制。事务拒绝符号链接、目标冲突和复制期间发生变化的 Source，失败时不会留下半个目标。成功后新目录立刻进入普通 Asset import 与脚本 generation。

损坏当前格式、只读 mount 写入、Importer 冲突、Artifact closure 不完整和 observer failure 都明确报告。`Library` 可删除重建，不作为创作事实来源。
