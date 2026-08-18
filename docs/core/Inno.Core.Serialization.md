# Inno.Core.Serialization

[上一页：Reflection](Inno.Core.Reflection.md) · [Core 索引](README.md) · [下一页：Framework](Inno.Core.Framework.md)

`Inno.Core.Serialization` 提供确定性的二进制对象图格式、基于 attribute 的属性持久化，以及通过 TypeRegistry 自动发现的 Converter。它既支持“序列化完整 `ISerializable` 根对象”，也支持用 Writer/Reader 显式定义 schema。

## 初始化

`SerializationManager.Initialize()` 必须在 `AssemblyManager` 与 `TypeCacheManager` 之后调用。Converter Registry 会跟随 TypeCache 的事务刷新；热重载新增/替换 Converter 不需要业务层手动订阅事件。

```csharp
AssemblyManager.Initialize(assemblyOptions);
TypeCacheManager.Initialize();
SerializationManager.Initialize();
```

| SerializationManager 成员 | 说明 |
| --- | --- |
| `isInitialized` | Converter catalog 是否可用。 |
| `Initialize()` / `Shutdown()` | 创建或清空 Converter Registry。 |
| `GetProperties(ISerializable)` | 返回稳定排序且允许运行时读取的 `SerializedProperty`。 |
| `Serialize<T>(T, context?)` | 把 class `ISerializable` 根对象编码为 version-two bytes。 |
| `Deserialize<T>(ReadOnlySpan<byte>, context?)` | 创建并恢复一个新根对象。 |
| `Restore<T>(T target, ReadOnlySpan<byte>, context?)` | 将数据恢复到既有实例，适合身份必须保留的对象。 |
| `Encode(Action<SerializationWriter>, context?)` | 用手写 structured schema 编码。 |
| `Decode<TResult>(bytes, Func<SerializationReader,TResult>, context?)` | 用手写 schema 解码并返回结果。 |

## 属性序列化

类型实现空标记接口 `ISerializable`，需要持久化的 field/property 标注 `SerializablePropertyAttribute`：

```csharp
public sealed class PlayerState : ISerializable
{
    [SerializableProperty]
    public string playerName { get; set; } = string.Empty;

    [SerializableProperty(PropertyVisibility.Readonly)]
    public int score { get; private set; }

    [SerializableProperty(PropertyVisibility.Transient)]
    public bool isSelected { get; set; }

    [OnSerializableRestored]
    private void OnRestored()
    {
        // Rebuild non-serialized derived state after the whole operation succeeds.
    }
}
```

### Attributes 与接口

| API | 说明 |
| --- | --- |
| `ISerializable` | 声明引用类型参与属性序列化。 |
| `[SerializableProperty(visibility)]` | 标注 field/property；默认 `Show`。`propertyVisibility` 暴露规则，`order` 控制同一声明类型内的处理顺序。 |
| `[OnSerializableRestored]` | 标记无参实例方法，在完整 restore 成功后调用。 |
| `[RequiresSerializationConverter]` | 强制该 class 必须由显式 Converter 处理。 |
| `[SerializationExtension]` | 标记 Converter class，让 TypeCache/Registry 自动发现。 |

### PropertyVisibility

这是 `[Flags]` enum：

| 值 | Serialize | Deserialize | Runtime Get | Runtime Set |
| --- | --- | --- | --- | --- |
| `None` | 否 | 否 | 否 | 否 |
| `Show` | 是 | 是 | 是 | 是 |
| `Hide` | 是 | 是 | 否 | 否 |
| `Readonly` | 是 | 是 | 是 | 否 |
| `Transient` | 否 | 否 | 是 | 是 |
| `SerializeOnly` | 是 | 否 | 是 | 是 |
| `DeserializeOnly` | 否 | 是 | 是 | 是 |

也可直接组合底层 flags：`Serialize`、`Deserialize`、`RuntimeGet`、`RuntimeSet`。

### SerializedProperty

`name`、`propertyType`、`visibility`、`canRead`、`canWrite` 描述成员；`GetValue()` / `SetValue(object?)` 执行运行时访问，不符合 visibility 时抛 `InvalidOperationException`。

```csharp
foreach (SerializedProperty property in SerializationManager.GetProperties(state))
{
    object? value = property.GetValue();
    if (property.canWrite)
        property.SetValue(value);
}
```

### 成员顺序

CLR 将 field 与 property 存放在不同 metadata table 中，因此 `MetadataToken` 不能表达两者在 C# 源码中的交错顺序。Inno 脚本编译器会在生成运行时代码时自动把源码声明顺序写入 `SerializablePropertyAttribute.order`，所以 GameScripts/EditorScripts 中混合声明的 field 和 property 会按脚本顺序出现在 Inspector 和序列化管线中。

普通预编译程序集可以在需要跨 field/property 固定顺序时显式声明：

```csharp
[SerializableProperty(order = 0)]
public int firstProperty { get; set; }

[SerializableProperty(order = 1)]
public int secondField;
```

继承层级仍保持 base type 在前、derived type 在后；`order` 只比较同一个 declaring type 内的成员。

## SerializationContext

Context 是不可变的、按“精确契约类型”索引的操作依赖容器：

| 成员 | 说明 |
| --- | --- |
| `SerializationContext.empty` | 空 context。 |
| `With<TContext>(value)` | 返回包含/替换该精确类型的新 context。 |
| `TryGet<TContext>(out value)` | 尝试按精确类型取值，不按派生关系搜索。 |
| `GetRequired<TContext>()` | 缺少时抛异常。 |

```csharp
SerializationContext context = SerializationContext.empty
    .With<IAssetReferenceResolver>(resolver);
byte[] bytes = SerializationManager.Serialize(state, context);
```

## 自定义 Converter

```csharp
[SerializationExtension]
public sealed class RangeConverter : SerializationConverter<Range>
{
    public override void Write(SerializationWriter writer, Range value)
    {
        writer.Write("start", value.Start.Value);
        writer.Write("end", value.End.Value);
    }

    public override Range Read(SerializationReader reader)
        => new(reader.Read<int>("start"), reader.Read<int>("end"));
}
```

`SerializationConverter<T>` 的公开扩展点：

- `Write(SerializationWriter, T)`：写一个值。
- `Read(SerializationReader)`：创建一个值。
- `Restore(SerializationReader, T target)`：可选覆盖，原位恢复已有值；默认抛 `NotSupportedException`。

Converter 应为无状态 class，并提供无参构造函数。冲突、构造失败或候选 DLL 缺依赖时，新 Registry 不会激活。

## SerializationWriter

`context`、`path`、`valueType` 提供当前操作信息。Writer 在 callback 结束后失效，不应缓存。

| 方法 | 说明 |
| --- | --- |
| `Write<TValue>(name, value)` | 经统一 value pipeline 写命名值；名称必须非空且唯一。 |
| `WriteObject(name, Action<SerializationWriter>)` | 写一个结构化子对象。 |
| `WriteObjectArray<T>(name, values, writeElement)` | 写有序结构化对象数组。 |
| `WriteProperties(ISerializable)` | 把对象上标注的成员写入当前 object。 |

## SerializationReader

Reader 同样公开 `context`、`path`、`valueType`，仅在当前 decode/restore callback 内有效。

| 方法 | 说明 |
| --- | --- |
| `Contains(name)` | 是否存在成员。 |
| `Read<TValue>(name)` | 读取必需值；缺少或类型错误时抛异常。 |
| `TryRead<TValue>(name, out value)` | 缺少成员时返回 `false`。 |
| `ReadObject(name)` | 读取结构化子对象。 |
| `ReadObjectArray(name)` | 读取结构化对象数组。 |
| `RestoreProperties(ISerializable)` | 将当前对象的属性数据恢复到既有实例。 |
| `OnCompleted(Action)` | 整个 decode 成功后调用；用于解析图引用等延迟工作。 |

## 手写 Schema 示例

```csharp
byte[] bytes = SerializationManager.Encode(writer =>
{
    writer.Write("schemaVersion", 1);
    writer.WriteObjectArray("points", points, (item, point) =>
    {
        item.Write("x", point.x);
        item.Write("y", point.y);
    });
});

Vector2[] decoded = SerializationManager.Decode(bytes, reader =>
    reader.ReadObjectArray("points")
        .Select(item => new Vector2(item.Read<float>("x"), item.Read<float>("y")))
        .ToArray());
```

## 约束

- Writer/Reader 都是 operation-scoped；在操作外使用会失败。
- 循环引用和外部对象身份通常需要 Converter + context + `OnCompleted` 协作解决。
- Restore callback 只在整个操作成功后提交完成通知；异常时不会执行 completion callbacks。
- 格式当前称为 version two；更改二进制 schema 时必须保持兼容或明确提供迁移。
