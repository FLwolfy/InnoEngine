# Inno.Assets.Core

[上一页：Inno.Assets](Inno.Assets.md) · [Assets 索引](README.md) · [下一页：Assets.File](Inno.Assets.File.md)

`Inno.Assets.Core` 只定义不依赖文件系统或 Importer 实现的公共契约。它不选择 DLL、不扫描路径，也不持有可变 Catalog。

## AssetObject

所有运行时资产派生自 `AssetObject`。重要成员：

| 成员 | 语义 |
| --- | --- |
| `sourcePath` | 当前 source-relative path；移动时由 host 原位更新。 |
| `name` | 有路径时为完整文件名，否则为 CLR 类型名。 |
| `identity` | persistent/runtime Identity。 |
| `isMissing` | source 当前不可用或旧类型已被替换。 |
| `contentVersion` | canonical 内容每次成功提交后变化。 |
| `runtimePayload` | Importer 的 runtime named output 的只读视图。 |
| `OnRuntimePayloadChanged` | 新 payload 提交后的 `protected virtual` 扩展点。 |
| `OnUnloading` | 释放 runtime resource 前调用一次。 |

```csharp
public sealed class AudioAsset : AssetObject
{
    private AudioBuffer? m_buffer;

    protected override void OnRuntimePayloadChanged(
        ReadOnlyMemory<byte> previousPayload,
        ReadOnlyMemory<byte> currentPayload)
    {
        m_buffer?.Dispose();
        m_buffer = AudioBuffer.Decode(currentPayload.Span);
    }

    protected override void OnUnloading()
    {
        m_buffer?.Dispose();
        m_buffer = null;
    }
}
```

## Catalog 与 artifact 快照

### AssetImportStatus

| 值 | 说明 |
| --- | --- |
| `Unsupported` | 当前没有 Importer；没有假 artifact。 |
| `Pending` | 等待 import/commit。 |
| `Imported` | current artifact 有效。 |
| `Failed` | 最新 import 失败；diagnostics 可读，旧 successful artifact 可继续服务。 |
| `Missing` | source 不可用，persistent identity 仍保留。 |
| `Conflict` | metadata/identity 冲突，系统拒绝猜测或覆盖。 |

`AssetSourceKind` 区分 `File` 与 `Directory`。

### AssetArtifactKey

`AssetArtifactKey` 是规范化为大写的不可变内容键，支持 value equality、`==`、`!=` 与 `isEmpty`。它不是 generation number，不写入 Scene/Prefab 类型状态。

### AssetArtifactInfo

描述一个 named output：`key`、`outputName`、`absolutePath`、`contentHash`、`length`。`absolutePath` 指向 `Library` 内不可变缓存，不应作为可移植持久引用保存。

### AssetInfo

`AssetInfo` 是不可变 Catalog snapshot：`persistentId`、`relativePath`、`sourceKind`、`status`、`importerId`、`stableAssetTypeId`、`artifactKey`、`lastSuccessfulArtifactKey`、`diagnostics`。它适合 Editor、build graph 和诊断读取；不提供修改 Catalog 的 setter。

## 变更契约

`AssetChangeKind`：`Added`、`Modified`、`Moved`、`Removed`、`Missing`、`Replaced`、`StatusChanged`。

`AssetChange` 同时携带 persistent ID、当前路径和移动前路径。`AssetChangeSet` 用单调递增 `revision` 包装一次原子提交后的完整变更列表。

```csharp
AssetManager.Changed += changeSet =>
{
    foreach (AssetChange change in changeSet.changes)
    {
        if (change.kind == AssetChangeKind.Moved)
            Console.WriteLine($"{change.oldRelativePath} -> {change.relativePath}");
    }
};
```

## AssetDependency 与引用诊断

`AssetDependency` 的 equality 只使用 `persistentId`；`TypeRef type` 和 `lastKnownPath` 用于类型验证、恢复和诊断。内存协议不再保存裸 runtime ID/Stable Guid，序列化 converter 只把 `type.stableId` 写为 `stableTypeId`，不会写 runtime hint。含 `TypeRef` 的构造器与属性从 Scripting API facade 精确排除，因此 `TypeRef` 本身不向脚本导出。路径改变不改变依赖身份。

`AssetReferenceInfo`/`AssetReferenceLocation` 描述引擎已知引用位置，不等价于 CLR GC 引用计数。`AssetReferenceKind` 包含资产依赖、序列化属性、Scene、Prefab、Editor 与 runtime subsystem 等来源。

## Runtime host 边界

`AssetRuntimeHost` 是最小 public host bridge，用来提交 canonical state、更新 source path 和释放资源。它公开是为了避免 friend assembly，不会由任何 `Properties/ScriptingApi.cs` 导出。游戏脚本只能看到显式 facade exports，不能借此修改资产内部状态。
