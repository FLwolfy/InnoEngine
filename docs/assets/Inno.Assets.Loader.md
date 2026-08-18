# Inno.Assets.Loader

[上一页：Assets.File](Inno.Assets.File.md) · [Assets 索引](README.md) · [下一页：Assets.Serialization](Inno.Assets.Serialization.md)

Loader 项目负责 Importer 自动发现、源文件导入、`.imeta` 持久 catalog、artifact、canonical asset cache、依赖图、missing placeholder、异步 load 合并与 unused collection。

## 编写 Importer

Importer class 标注 `[AssetImporterExtension]` 并派生 `AssetImporter<TAsset>`：

```csharp
[AssetImporterExtension]
public sealed class CsvImporter : AssetImporter<TableAsset>
{
    public override string importerId => "com.example.csv";
    public override int version => 2;
    public override IReadOnlyList<string> supportedExtensions { get; } = [".csv"];

    protected override AssetImportResult<TableAsset> Import(AssetImportContext context)
    {
        context.DependsOnSource("Schemas/table.schema");
        string source = context.ReadUtf8Text();
        TableAsset asset = Parse(source);
        return new AssetImportResult<TableAsset>(asset, Compile(asset));
    }

    protected override bool TryExport(TableAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = Encoding.UTF8.GetBytes(WriteCsv(asset));
        return true;
    }
}
```

Importer 必须是具体 class 并有无参构造函数。TypeCache/Registry 会在 assembly reload 候选阶段发现、验证冲突并实例化；失败时旧 Registry 保持活动。

## AssetImporter API

### 非泛型基类

| 属性 | 说明 |
| --- | --- |
| `importerId` | 稳定实现 ID，默认 full type name；持久缓存契约，建议显式固定。 |
| `version` | 导入算法 schema 版本，默认 1；修改持久输出时递增。 |
| `targetAssetType` | 产生的具体 `AssetObject` 类型。 |
| `supportedExtensions` | 支持的规范扩展名列表。 |

### AssetImporter&lt;TAsset&gt;

- `targetAssetType` sealed 为 `typeof(TAsset)`。
- `protected abstract Import(AssetImportContext)`：生成 managed asset 与 runtime payload。
- `protected virtual TryExport(TAsset, out byte[])`：默认返回 false；支持 Save 时覆盖。

`AssetImportResult<TAsset>` readonly struct 的构造参数和属性为 `asset`、`runtimePayload`；asset 不能为 null。

## AssetImportContext

| 属性/方法 | 说明 |
| --- | --- |
| `relativePath` / `absolutePath` | 当前源相对/绝对路径。 |
| `sourceBytes` | 原始源 bytes。 |
| `sourceHash` | 确定性内容 hash。 |
| `extension` | lower-case 扩展名。 |
| `ReadUtf8Text()` | UTF-8 解码并去掉可选 BOM。 |
| `DependsOnAsset(string)` | 声明 runtime asset dependency 路径。 |
| `DependsOnAsset(AssetDependency)` | 声明已有 persistent descriptor。 |
| `DependsOnSource(string)` | 该源文件变化时使本 artifact 失效。 |
| `DependsOnArtifact(Guid)` | 被引用 artifact owner 变化时失效。 |
| `DependsOnCustomInput(key,fingerprint)` | 声明任意确定性构建输入。 |

Runtime dependency 会进入加载引用图；Source/Artifact/Custom input 属于 import invalidation graph，两者不要混淆。

## AssetLoader

直接构造适合工具与测试；应用层通常用 [AssetManager](Inno.Assets.md)。

```csharp
using AssetLoader loader = new(assetRoot, artifactRoot);
loader.Rescan();
AssetObject? asset = loader.Load("Data/file.bin", typeof(BinaryAsset));
```

### 状态和主操作

| API | 说明 |
| --- | --- |
| 构造 `AssetLoader(assetRoot, artifactRoot)` | 规范根目录并创建目录。 |
| `assetRoot` / `artifactRoot` | 绝对路径。 |
| `AssetReloaded` | canonical asset 原位提交新内容后触发。 |
| `Import(path)` | 导入单个源；无文件/无 Importer 时 false。 |
| `Rescan()` | 对账源、metadata、artifacts 和 catalog。 |
| `Dispose()` | 释放缓存资产、Registry 和同步资源。 |

### 加载与引用

| API | 说明 |
| --- | --- |
| `Load(path/id, Type)` | 返回兼容 canonical asset 或 null。 |
| `TryLoad(path/id, Type, out asset)` | try 形式。 |
| `LoadAsync(path/id, Type, token)` | 合并同 key 的并发 load；按请求 Type 检查结果。 |
| `ResolveReference(persistentId, stableTypeId, lastKnownPath, expectedType)` | 恢复序列化引用，找不到时创建 persistent missing placeholder。 |

### 保存、变更、查询

| API | 说明 |
| --- | --- |
| `Save(asset)` / `Save(path,asset)` | 由匹配 Importer 导出源 bytes。 |
| `ApplySourceChanges(IReadOnlyList<AssetChangedEvent>)` | 应用 File 层的规范化 batch。 |
| `TryGetPersistentId(path,out id)` | metadata-only 查询。 |
| `TryGetAssetType(path,out type)` | metadata-only 类型解析。 |
| `GetLoadedPaths()` | canonical load 路径快照。 |
| `GetDependencies(asset,recursive)` | runtime dependency descriptor。 |
| `GetReferenceInfo(asset)` | 引用诊断快照。 |
| `UnloadUnusedAssets()` | 回收无外部引用的 canonical entries。 |

## 内置 Importer

| Importer | 扩展名 | Asset | language/payload |
| --- | --- | --- | --- |
| Text | `.txt`, `.json`, `.yaml`, `.yml`, `.md`, `.xml` | `TextAsset` | UTF-8；hint 为 plain/json/yaml/markdown/xml |
| Binary | `.bytes`, `.bin`, `.dat` | `BinaryAsset` | 原始 bytes |

两者在 `Inno.Assets.Loader.Importers` namespace 中为 internal 实现，由 Registry 自动发现；不再存在 BuiltIn package API，也不包含 PNG/Shader Importer。

## 缓存与热重载规则

- `importerId + version` 是跨进程持久 artifact cache 契约。
- 同一运行中若 importer implementation Type/generation 改变，即使 version 不变，也会标记相关资产重新导入。
- source hash 或声明的 import dependency fingerprint 改变会使 artifact 失效。
- candidate Importer ID/extension 冲突会阻止新 assembly generation 激活。
