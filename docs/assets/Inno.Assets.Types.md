# Inno.Assets.Types

[上一页：Assets.Serialization](Inno.Assets.Serialization.md) · [Assets 索引](README.md) · [Wiki 首页](../README.md)

内置资产类型只保留 `TextAsset` 与 `BinaryAsset`。两者都派生 `AssetObject`、带显式 Stable Type ID，并由 Loader 内置 Importer 生成。

## TextAsset

```csharp
TextAsset text = new("{\"enabled\":true}", "json");
Console.WriteLine(text.content);
```

| 成员 | 说明 |
| --- | --- |
| `TextAsset()` | 创建空文本，`content=""`、`languageHint="plain"`。 |
| `TextAsset(string content, string languageHint="plain")` | null content 归为空串，null hint 归 plain。 |
| `content` | 解码文本，private setter，参与序列化。 |
| `languageHint` | 格式提示，private setter，参与序列化。 |

内置 Importer hint：`.json → json`、`.yaml/.yml → yaml`、`.xml → xml`、`.md → markdown`，其他支持文本扩展名为 `plain`。

Stable Type ID：`907c91cf-215b-42f4-9243-26d9666b231a`。

## BinaryAsset

```csharp
BinaryAsset binary = AssetManager.Load<BinaryAsset>("Data/blob.bytes");
ReadOnlyMemory<byte> payload = binary.runtimePayload;
Debug.Assert(binary.byteLength == payload.Length);
```

| 成员 | 说明 |
| --- | --- |
| `BinaryAsset()` | 空 descriptor。 |
| `BinaryAsset(int byteLength)` | 指定 payload 长度 descriptor。 |
| `byteLength` | 导入时记录的长度，private setter，参与序列化。 |

真实 bytes 位于继承的 `runtimePayload`，`byteLength` 是可序列化描述信息。

Stable Type ID：`5298dd91-e9a7-4298-a343-bf8a6c5fc779`。

## 支持扩展名

| 类型 | 扩展名 |
| --- | --- |
| TextAsset | `.txt`, `.json`, `.yaml`, `.yml`, `.md`, `.xml` |
| BinaryAsset | `.bytes`, `.bin`, `.dat` |

`TextureAsset` 与 `ShaderAsset` 已移除。如果 Rendering 需要纹理/Shader 资产，应由对应模块定义自己的 AssetObject 和 Importer，而不是让基础 Assets.Types 无边界增长。

## 创建与保存

构造函数创建的对象尚无 `sourcePath` 或 runtime identity。通过显式路径保存后，Loader 才会写源文件、metadata，建立 canonical runtime state：

```csharp
TextAsset created = new("Hello", "plain");
bool saved = AssetManager.Save("Notes/hello.txt", created);
```
