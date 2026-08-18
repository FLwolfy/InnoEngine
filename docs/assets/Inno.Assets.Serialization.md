# Inno.Assets.Serialization

[上一页：Assets.Loader](Inno.Assets.Loader.md) · [Assets 索引](README.md) · [下一页：Assets.Types](Inno.Assets.Types.md)

这个项目把 Asset 引用接入 [Core Serialization](../core/Inno.Core.Serialization.md)。内部 Converter 持久化 `persistentId + stableTypeId + lastKnownPath`，而不是嵌入整个 AssetObject；解码时通过已配置 resolver 返回 canonical instance 或 missing placeholder。

## AssetDependencyCollection

在序列化“本身会成为资产源”的对象时，把 collection 放入 context，即可同时收集遇到的 AssetObject 引用：

```csharp
AssetDependencyCollection dependencies = new();
SerializationContext context = SerializationContext.empty.With(dependencies);

byte[] bytes = SerializationManager.Serialize(assetDocument, context);
foreach (AssetDependency dependency in dependencies.dependencies)
    Console.WriteLine(dependency.persistentId);
```

唯一公开属性 `dependencies` 返回按 persistent ID 稳定排序的快照。同一 persistent ID 只保留一个描述。

## AssetSerializationServices

公开 `SetReferenceResolver(Func<Guid, Guid, string, Type, string, AssetObject>?)`：

参数依次为 persistent ID、Stable Type ID、last-known path、expected CLR type 和 property path。传 null 清除 resolver。

`AssetManager.Initialize` 会自动安装 resolver，Shutdown 自动清除。只有在独立工具宿主中绕过 AssetManager 时才需要手动设置：

```csharp
AssetSerializationServices.SetReferenceResolver(
    (id, stableTypeId, path, expectedType, propertyPath) =>
        loader.ResolveReference(id, stableTypeId, path, expectedType));
```

没有 resolver 时读取 AssetObject reference 会抛 `InvalidOperationException`，而不会静默返回 null。

## 内部 Converter 行为

虽然 Converter 类型是 internal，不属于调用 API，但其持久 schema 是兼容性契约：

- `AssetDependency`：`persistentId`、`stableTypeId`、`lastKnownPath`。
- `AssetObject` 引用：相同三字段。
- 写 AssetObject 时若 context 中有 `AssetDependencyCollection`，自动收集引用。
- 空 persistent ID 或无法从 TypeCache 取得 Stable ID 会拒绝序列化。
- 读取时 property path 会进入错误诊断。

Converter 标注 `[SerializationExtension]`，随 TypeCache/Converter Registry 刷新，无需 package wrapper 或显式 Scene serializer 注册。

## 生命周期与安全

- Resolver 是进程级静态服务，Host 必须在关闭 Loader 前清除。
- Resolver delegate 会强引用 target；插件提供自定义 resolver 时必须在 unload 前移除。
- last-known path 用于诊断/fallback，不是引用主键；移动文件后 persistent ID 仍是权威身份。
