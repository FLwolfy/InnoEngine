# Inno.Assets.Pipeline

[Assets 索引](README.md) · [Runtime Assets](Inno.Assets.md) · [Plugins](../plugins/Inno.Plugins.Authoring.md)

## 职责与边界

该 authoring project 拥有 Source Mount、文件索引/watcher、Importer、Build Processor、`.imeta`、依赖图、Catalog、CAS Artifact、canonical authoring object 和 runtime closure export。Player 不引用它。

## 公开 API

| 分组 | 主要 API |
| --- | --- |
| Composition | `AssetPipeline`, `AssetPipelineOptions`, `AssetPipelineMode`, `AssetSourcePolicy`, `AssetCacheOptions` |
| Source | `AssetSourceMount`, `AssetSourceMountTransaction`, `AssetFileSystem`, `AssetFileEntry`, `AssetChangedEvent` |
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

所有 mutation 必须在构造线程执行。Save、Import、Move、Delete、CreateDirectory 和 source candidate commit 各自发布一个 revision；后台 `ExportRuntimeArtifactsAsync` 只使用 owner thread 捕获的 immutable Serialization generation。

损坏当前格式、只读 mount 写入、Importer 冲突、Artifact closure 不完整和 observer failure 都明确报告。`Library` 可删除重建，不作为创作事实来源。
