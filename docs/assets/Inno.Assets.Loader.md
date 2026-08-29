# Inno.Assets.Loader

[上一页：Assets.File](Inno.Assets.File.md) · [Assets 索引](README.md) · [下一页：Assets.Serialization](Inno.Assets.Serialization.md)

Loader 负责 `.imeta`、Catalog、content-addressed artifact store、Importer/Build Processor Registry、依赖失效、canonical cache 和 missing/replacement 语义。应用业务优先使用 [AssetManager](Inno.Assets.md)。

## 编写 Importer

```csharp
[AssetImporterExtension]
public sealed class CsvImporter : AssetImporter<TableAsset>
{
    public override string importerId => "com.example.csv";
    public override IReadOnlyList<string> supportedExtensions { get; } = [".csv"];

    protected override async ValueTask ImportAsync(
        AssetImportContext context,
        AssetImportWriter<TableAsset> output,
        CancellationToken cancellationToken)
    {
        output.DependsOnSource(AssetPath.Project("Schemas/table.schema"));
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
| `targetAssetType` | 具体 `AssetObject` 类型。 |
| `supportedExtensions` | 接受的 source extension。 |
| `ImportAsync` | 在 candidate writer 上完成单 source import。 |
| `ExportAsync` | 可选 Save 扩展点；默认不支持。 |

已移除 `AssetImportResult<T>`。Writer API 能表达多个 named outputs、依赖与 diagnostics，并保证产物全部准备完成后才 commit。

## Import context/writer

`AssetImportContext` 提供 `assetPath`、`absolutePath`、`persistentId`、`sourceBytes`、`sourceHash`、`extension`、`ReadUtf8Text()`。`persistentId` 在 Importer 执行前已经确定，可用于生成需要随 source rename 保持稳定的 manifest。需要读取另一个运行时 Asset 时使用 `ResolveDependency<TAsset>(path)`：Loader 会通过同一 canonical cache 解析目标，并自动记录 Asset dependency；缺失、类型不匹配或循环依赖会让本次 candidate Import 明确失败，而不会提交半成品。

Importer 读取 include、schema 等 companion source 时使用 `ReadSourceBytes(path)` 或 `ReadSourceUtf8Text(path)`。两者从当前隔离的 Source Mount candidate 读取稳定 snapshot 并自动记录 import dependency；Plugin 跨 Mount 路径必须写成 `plugin.id::local/path`，且 owning Plugin 必须声明该依赖。Importer 不应自行访问 `AssetManager.sourceMounts` 或拼接 Library/Plugin 物理路径，否则候选激活期间会错误地读取 active generation。

`AssetImportWriter<T>`：

- `SetAsset`
- `ResolveDependency<TAsset>`（位于 context；解析并声明运行时 Asset dependency）
- `WriteArtifactAsync`
- `DependsOnAsset(path/descriptor)`
- `DependsOnSource`
- `DependsOnArtifact`
- `DependsOnCustomInput`
- `ReportDiagnostic`

Runtime asset dependency 影响加载保活和引用恢复；Source/Artifact/Custom dependency 影响 import fingerprint。Companion `.pdb`/`.deps.json` 就通过 `DependsOnSource` 进入 DLL Importer 的失效输入。

Importer 抛异常时 transaction 不提交半成品。Catalog 状态变为 `Failed`、保存 diagnostic，并继续保留 last-successful artifact/canonical state。

Loader 在每次 Catalog commit 后把所有带 persistent ID 的当前 Import 状态同步到 `Inno.Core.Diagnose`。同一 Asset 的下一次成功 Import 会自动清除旧报告；identity conflict 和 Importer 返回的 warning 同样按 Asset ID 独立呈现。单纯删除 Asset 只形成 Catalog tombstone，不向 Console 制造 warning；只有 Engine 实际解析到该 missing reference 时才发布独立的 `Asset Reference` Diagnostic，恢复同一 ID 后自动清除。Unsupported source 没有 persistent ID，因此只保留 Catalog 状态，不伪造全局诊断目标。

## Aggregate Build Processor

聚合输出使用 `AssetBuildProcessor<TDefinition>`：

```csharp
[AssetBuildProcessorExtension]
public sealed class AtlasProcessor : AssetBuildProcessor<AtlasDefinitionAsset>
{
    public override string processorId => "com.example.atlas";

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

`AssetManager.BuildAsync` 根据 definition runtime type 找到 Processor。Build key 包含 processor ID、implementation MVID、definition type、definition identity、source hash、序列化状态、runtime payload 和全部输入 artifact key。代码或定义内容变化会自然使缓存失效，不要求也不允许开发者维护持久化 schema/version 字段。它适合 Script Assembly、Shader Library、Atlas 等多输入 derived artifact。

## AssetLoader 公开 API

直接使用主要面向测试和离线工具：

```csharp
using var loader = new AssetLoader(assetRoot, libraryRoot);
loader.Rescan();
TextAsset? asset = loader.Load(
    AssetPath.Project("Data/file.txt"),
    typeof(TextAsset)) as TextAsset;
```

| 分类 | API |
| --- | --- |
| 根目录 | `assetRoot`、`libraryRoot`、派生 `artifactRoot` |
| 导入 | `Import`、`Rescan`、`ApplySourceChanges`、`RefreshRegistries` |
| 加载 | `Load`/`TryLoad`/`LoadAsync`（`AssetPath` 或 ID） |
| 保存 | `Save(asset)`、`Save(path,asset)` |
| 查询 | `TryGetInfo`、`TryGetArtifact`、`TryGetPersistentId`、`TryGetAssetType` |
| 构建 | `BuildAsync` |
| 引用 | `ResolveReference`、`GetDependencies`、`GetReferenceInfo` |
| 回收 | `UnloadUnusedAssets`、`CollectArtifacts`、`WaitForIdle` |

`LoadAsync` 不是同步 `Load` 的包装。Loader 按 `AssetPath` 或 persistent ID 合并同一时刻的后台加载任务，导入和 artifact materialization 在 worker 执行；所有等待者最终取得同一 canonical instance。某个等待者取消时只取消自己的 await，共享任务继续服务其他调用者。Loader 退休时先停止接受请求，再等待已接受任务完成，避免 generation 切换或关闭期间的 use-after-dispose。

## `.imeta`

Sidecar 只保存不可重建、应进入版本控制的内容：

```text
persistentId
sourceKind
importerId
importerSettingsBytes
```

不再保存 relative path、source hash、implementation MVID、import status、dependency graph、asset state 或 artifact payload。路径来自 sidecar 所在位置，其余可重建信息进入 Library Catalog。

文件夹也有 `.imeta`；`Assets` root 本身没有。`.imeta` 不携带 schema version，也不包含旧格式迁移分支；当前源码和当前引擎始终使用同一份直接契约。

## Catalog 与 journal

```text
Library/AssetDatabase/
├─ Catalog.snapshot
└─ Catalog.journal
```

Catalog 保存 path/ID index、状态、diagnostics、fingerprint、current/last artifact、stable type、dependency graph 和 tombstone。每次 commit 先写 journal，再原子替换 snapshot；启动时优先恢复完整 journal，损坏或不完整尾部不会让 host 崩溃。

Catalog load/commit 持续失败时由 `Asset Catalog` Diagnostic 表示当前降级状态，同时把第一次完整异常写入 Log；下一次成功 commit 会清除 Diagnostic。Aggregate Build 采用独立的 `Asset Build` target group，Build Processor 再次成功且不再返回 warning 后会清除原报告。

### Source change detection

Catalog 同时保存 source 与 source dependency 的 cheap file stamp：文件长度、UTC 修改时间和创建时间。普通 `Load(path)` 先比较 stamp；stamp 未变化时完全信任 Catalog 和 artifact，不打开 source 内容。stamp 变化时才读取稳定 source snapshot 并计算 SHA-256：

- hash 未变化时只更新 Catalog stamp，不运行 Importer；
- hash 变化时运行 Importer，并在完整 artifact commit 后切换 Catalog；
- Watcher 的明确 change 事件直接进入 import reconciliation；
- Watcher overflow/error 触发全量目录 reconciliation；
- Importer 执行期间 source stamp 再次变化会放弃 candidate，避免把旧内容与新 stamp 一起提交。

`Load(persistentId)` 继续直接读取一致的 Catalog/artifact snapshot；外部变更在 owner-thread `AssetManager.Update()` commit 后对其可见。

## Content-addressed artifact store

```text
Library/Artifacts/ab/cd/<artifact-key>/
├─ manifest
└─ outputs/
   ├─ 0000.bin
   └─ 0001.bin
```

Manifest 把稳定 output name 映射到物理文件，并记录 content hash/length。所有 output 先写 `.staging`，完整后用 directory move 原子提交。同一 import fingerprint 和相同 outputs 得到同一 key；source rename 不改变 key。

当前约定的 output name 包括 `asset-state`、`runtime`、`source`、`assembly`、`symbols`、`dependencies`、`diagnostics`、`type-manifest`、`preview`，Importer/Processor 也可定义自己的稳定名字。

## Registry 与 hot reload

- Importer/Processor 都通过 `TypeRegistry<TSnapshot>` 自动发现。
- 活动 TypeCache snapshot 未变时读取现有 frozen registry。
- 同一进程内 importer implementation generation 改变会强制相关 source reimport，即使显式 `version` 未提升。
- Import/Build fingerprint 直接包含实现 MVID 与当前定义内容；MVID 不写入 `.imeta`，也没有手工 `version` 协议。
- 候选 registry 冲突或构造失败时，Assembly reload 不会提交半更新 snapshot。
- AssetManager 作为 Assembly Catalog transaction participant，在候选 Registry 激活后、Catalog 发布前执行 Source 对账；后续失败会在旧 TypeCache 恢复后自动重新对账，详见 [Inno.Assets](Inno.Assets.md)。

## 内置 Importer

| Importer | 扩展名 | Asset |
| --- | --- | --- |
| Text | `.txt`, `.json`, `.yaml`, `.yml`, `.md`, `.xml` | `TextAsset` |
| Binary | `.bytes`, `.bin`, `.dat` | `BinaryAsset` |

没有 wildcard Binary fallback。Rendering 的 Shader、Material、Pipeline、Mesh 与 Texture Importer 位于独立的 `Inno.Rendering.Assets` 项目，详见 [Rendering Assets](../render/Inno.Rendering.Assets.md)。
