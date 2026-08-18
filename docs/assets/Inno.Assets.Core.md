# Inno.Assets.Core

[上一页：Inno.Assets](Inno.Assets.md) · [Assets 索引](README.md) · [下一页：Assets.File](Inno.Assets.File.md)

`Inno.Assets.Core` 定义所有资产共有的运行时对象、持久依赖描述和引用诊断模型。它不负责磁盘 IO 或 Importer 选择。

## AssetObject

所有资产类型派生自 `AssetObject`；它实现 `ISerializable` 与 `IIdentityObject`。

| 成员 | 说明 |
| --- | --- |
| `sourcePath` | 源根目录下的规范化相对路径；序列化为 Hide。 |
| `name` | 有路径时取文件名，否则为 CLR 类型名。 |
| `identity` | 当前 persistent/runtime Identity。 |
| `isMissing` | 是否是无法解析真实文件的持久 placeholder。 |
| `contentVersion` | 每次成功提交运行时内容后递增/更新。 |
| `runtimePayload` | Importer 生成的只读 runtime artifact bytes。 |
| `OnRuntimePayloadChanged(previous,current)` | `protected virtual`，新 payload 提交后调用。 |
| `OnUnloading()` | `protected virtual`，运行时资源释放前只调用一次。 |

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

不要保存 `runtimePayload` 内部 array 的可变引用；公开 API 只给 `ReadOnlyMemory<byte>`。

## AssetDependency

readonly value descriptor：

```csharp
AssetDependency dependency = new(
    persistentId,
    stableTypeId,
    "Textures/player.png");
```

| 属性 | 说明 |
| --- | --- |
| `persistentId` | 被引用资产的持久身份；构造时不可为空。 |
| `stableTypeId` | 预期资产类型的 Stable Type ID。 |
| `lastKnownPath` | 诊断与 fallback 路径。 |

Equality、hash 和 `==/!=` 只按 `persistentId`，因此路径变化不改变依赖身份。

## 引用诊断

`AssetReferenceInfo` 由 Loader 构造，包含：

- `persistentId`
- `sourcePath`
- `contentVersion`
- `isLoaded`
- `lastSweepReachability`：上一次 unused sweep 是否发现外部 managed 引用，未检查时 null。
- `knownReferenceCount`
- `references`：`AssetReferenceLocation` 列表。

`AssetReferenceLocation` 属性：

| 属性 | 说明 |
| --- | --- |
| `kind` | 引用分类。 |
| `ownerId` | owner 持久 ID，可无。 |
| `ownerName` | 可读 owner 名称。 |
| `propertyPath` | 序列化/子系统相对路径。 |

`AssetReferenceKind` 值：`AssetDependency`、`SerializedProperty`、`SceneResource`、`PrefabSource`、`Editor`、`RuntimeSubsystem`。

engine-known reference 仅用于诊断，不等于 CLR GC 的强引用数量，也不直接决定能否 unload。

## Missing placeholder

序列化引用的 persistent ID 存在，但源资产暂不可用时，Loader 可返回类型兼容、`isMissing=true` 的 canonical placeholder。这样引用身份不会消失；资源重新出现后可原位恢复内容。
