# Inno.Assets.Loader

[上一页：Assets.File](Inno.Assets.File.md) · [Assets 索引](README.md) · [下一页：Assets.Serialization](Inno.Assets.Serialization.md)

Loader 负责 `.imeta`、versioned Catalog、content-addressed artifact store、Importer/Build Processor Registry、依赖失效、canonical cache 和 missing/replacement 语义。应用业务优先使用 [AssetManager](Inno.Assets.md)。

## 编写 Importer

```csharp
[AssetImporterExtension]
public sealed class CsvImporter : AssetImporter<TableAsset>
{
    public override string importerId => "com.example.csv";
    public override int version => 2;
    public override IReadOnlyList<string> supportedExtensions { get; } = [".csv"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<TableAsset> output,
        CancellationToken cancellationToken)
    {
        output.DependsOnSource("Schemas/table.schema");
        TableAsset table = Parse(context.ReadUtf8Text());
        output.SetAsset(table);
        await output.WriteArtifactAsync(
            "runtime",
            Compile(table),
            cancellationToken);
        await output.WriteArtifactAsync(
            "preview",
            CreatePreview(table),
            cancellationToken);
    }

    protected override ValueTask<ReadOnlyMemory<byte>?> ExportAsync(
        TableAsset asset,
        CancellationToken cancellationToken)
        => ValueTask.FromResult<ReadOnlyMemory<byte>?>(WriteCsv(asset));
}
```

Importer 必须是 concrete class、有可访问无参构造函数、标注 `[AssetImporterExtension]`。Registry 在 TypeCache snapshot 上构建并验证 importer ID 与 extension 冲突。

### AssetImporter

| 成员 | 说明 |
| --- | --- |
| `importerId` | 稳定 importer contract ID；建议显式固定。 |
| `version` | 跨进程持久输出兼容版本。 |
| `targetAssetType` | 具体 `AssetObject` 类型。 |
| `supportedExtensions` | 接受的 source extension。 |
| `ImportAsync` | 在 candidate writer 上完成单 source import。 |
| `ExportAsync` | 可选 Save 扩展点；默认不支持。 |

已移除 `AssetImportResult<T>`。Writer API 能表达多个 named outputs、依赖与 diagnostics，并保证产物全部准备完成后才 commit。

## Import context/writer

`AssetImportContext` 提供 `relativePath`、`absolutePath`、`sourceBytes`、`sourceHash`、`extension`、`ReadUtf8Text()`。

`AssetImportWriter<T>`：

- `SetAsset`
- `WriteArtifactAsync`
- `DependsOnAsset(path/descriptor)`
- `DependsOnSource`
- `DependsOnArtifact`
- `DependsOnCustomInput`
- `ReportDiagnostic`

Runtime asset dependency 影响加载保活和引用恢复；Source/Artifact/Custom dependency 影响 import fingerprint。Companion `.pdb`/`.deps.json` 就通过 `DependsOnSource` 进入 DLL Importer 的失效输入。

Importer 抛异常时 transaction 不提交半成品。Catalog 状态变为 `Failed`、保存 diagnostic，并继续保留 last-successful artifact/canonical state。

## Aggregate Build Processor

聚合输出使用 `AssetBuildProcessor<TDefinition>`：

```csharp
[AssetBuildProcessorExtension]
public sealed class AtlasProcessor : AssetBuildProcessor<AtlasDefinitionAsset>
{
    public override string processorId => "com.example.atlas";
    public override int version => 3;

    protected override ValueTask BuildAsync(
        AssetBuildContext<AtlasDefinitionAsset> context,
        AssetArtifactWriter output,
        CancellationToken cancellationToken)
    {
        byte[] atlas = BuildAtlas(context.definition, context.inputs);
        return output.WriteAsync("runtime", atlas, cancellationToken);
    }
}
```

`AssetManager.BuildAsync` 根据 definition runtime type 找到 Processor。Build key 包含 processor ID/version/MVID、definition identity 和全部输入 artifact key。它适合 Script Assembly、Shader Library、Atlas 等多输入 derived artifact。

## AssetLoader 公开 API

直接使用主要面向测试和离线工具：

```csharp
using var loader = new AssetLoader(assetRoot, libraryRoot);
loader.Rescan();
TextAsset? asset = loader.Load("Data/file.txt", typeof(TextAsset)) as TextAsset;
```

| 分类 | API |
| --- | --- |
| 根目录 | `assetRoot`、`libraryRoot`、派生 `artifactRoot` |
| 导入 | `Import`、`Rescan`、`ApplySourceChanges`、`RefreshRegistries` |
| 加载 | `Load`/`TryLoad`/`LoadAsync`（path 或 ID） |
| 保存 | `Save(asset)`、`Save(path,asset)` |
| 查询 | `TryGetInfo`、`TryGetArtifact`、`TryGetPersistentId`、`TryGetAssetType` |
| 构建 | `BuildAsync` |
| 引用 | `ResolveReference`、`GetDependencies`、`GetReferenceInfo` |
| 回收 | `UnloadUnusedAssets`、`CollectArtifacts`、`WaitForIdle` |

## `.imeta` v3

Sidecar 只保存不可重建、应进入版本控制的内容：

```text
schemaVersion
persistentId
sourceKind
importerId
importerSettingsVersion
importerSettingsBytes
```

不再保存 relative path、source hash、implementation MVID、import status、dependency graph、asset state 或 artifact payload。路径来自 sidecar 所在位置，其余可重建信息进入 Library Catalog。

文件夹也有 `.imeta`；`Assets` root 本身没有。Loader 可读取旧 v2 meta 并保留 persistent ID。

## Catalog 与 journal

```text
Library/AssetDatabase/
├─ Catalog.snapshot
└─ Catalog.journal
```

Catalog 保存 path/ID index、状态、diagnostics、fingerprint、current/last artifact、stable type、dependency graph 和 tombstone。每次 commit 先写 journal，再原子替换 snapshot；启动时优先恢复完整 journal，损坏或不完整尾部不会让 host 崩溃。

## Content-addressed artifact store

```text
Library/Artifacts/ab/cd/<artifact-key>/
├─ manifest
└─ outputs/
   ├─ 0000.bin
   └─ 0001.bin
```

Manifest 把稳定 output name 映射到物理文件，并记录 content hash/length。所有 output 先写 `.staging`，完整后用 directory move 原子提交。同一 import fingerprint 和相同 outputs 得到同一 key；source rename 不改变 key。

当前约定的 output name 包括 `asset-state`、`runtime`、`source`、`assembly`、`symbols`、`dependencies`、`diagnostics`、`preview`，Importer/Processor 也可定义自己的稳定名字。

## Registry 与 hot reload

- Importer/Processor 都通过 `TypeRegistry<TSnapshot>` 自动发现。
- 活动 TypeCache snapshot 未变时读取现有 frozen registry。
- 同一进程内 importer implementation generation 改变会强制相关 source reimport，即使显式 `version` 未提升。
- 重启后的缓存兼容仍依赖开发者维护 `version`；MVID 不写入 `.imeta`。
- 候选 registry 冲突或构造失败时，Assembly reload 不会提交半更新 snapshot。

## 内置 Importer

| Importer | 扩展名 | Asset |
| --- | --- | --- |
| Text | `.txt`, `.json`, `.yaml`, `.yml`, `.md`, `.xml` | `TextAsset` |
| Binary | `.bytes`, `.bin`, `.dat` | `BinaryAsset` |

没有 wildcard Binary fallback，也没有 PNG/Shader Importer。
