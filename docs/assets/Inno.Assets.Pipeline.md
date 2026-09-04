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

## `~` 开发目录与安装态 `.isample`

Project Source Mount 中，名称以 `~` 开头的目录在 File Browser 中显示为 `ISAMPLE`，但仍使用普通创作语义：Asset Import、Catalog、Artifact、authoring 脚本编译、Editor 运行和 Play Mode 都会正常处理，完整 Project 导出为 `.iplugin` 时也会携带这些源文件。`AssetSample.HasSampleDirectoryName(path)` 只表达这个与 Source 无关的命名/显示分类，不表示该目录需要导入。`AssetSample.IsRuntimeExcluded(path, isDirectory)` 则统一表达 deployment 边界：Game 的 runtime Asset 与 runtime script closure 始终剔除任何 Source 下的 `~` 子树；普通 runtime Asset 若依赖其中内容，导出会因闭包不完整而明确失败，Startup Scene 位于其中时也会被明确拒绝。

只读 Plugin Source Mount 中，名称以 `~` 开头的目录才是逻辑 `.isample`。`AssetFileSystem` 仍索引目录及后代，`AssetFileEntry.isSample` 标记该目录本身，`isSampleContent` 标记完整子树；Editor 可以浏览它们，但 Asset Import/Catalog、Artifact 与 Plugin 脚本编译不会处理该子树。

`AssetPipeline.ImportSample(source)` 把选中的安装态 `.isample` 原子复制到 Project `Assets` 根，并完整保留所选根目录名及其所有前导 `~`。复制保留 `.imeta`，所以 Sample 内部 persistent reference 不会因导入断裂；`.abin` 与 source noise 不复制。事务拒绝 Project 自身的 `~` 目录、符号链接、目标冲突和复制期间发生变化的 Source，失败时不会留下半个目标。成功后 `~` 目录立刻按 Project 普通 authoring content 进入 Asset import 与脚本 generation，但仍不会进入 runtime deployment。

损坏当前格式、只读 mount 写入、Importer 冲突、Artifact closure 不完整和 observer failure 都明确报告。`Library` 可删除重建，不作为创作事实来源。
