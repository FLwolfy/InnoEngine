# Inno.Audio.Assets

[Audio 索引](README.md) · [Assets](../assets/Inno.Assets.md) · [Core contract](Inno.Audio.md) · [Runtime](Inno.Audio.Runtime.md)

`Inno.Audio.Assets` 是 authoring-only importer 项目。`AudioClipImporter` 通过 `[AssetImporterExtension]` 自动发现，直接把 `.wav`、`.flac` 和 `.mp3` 文件作为 `AudioClipAsset` 创作源，不创建 companion asset。

## 输出契约

| output | 内容 |
| --- | --- |
| `runtime` | codec、channels、sample rate、frame count 与 encoded byte length 的小型严格 payload。 |
| `audio-data` | 原始编码数据的独立、不可变 CAS Artifact；不放入 `AssetObject.runtimePayload`。 |

Importer 校验 WAV chunk、FLAC STREAMINFO 和 MP3 frame/header。截断、矛盾或无法确定播放 metadata 的源会导入失败并产生正常 Asset diagnostic，不生成可播放的半成品。

```csharp
AudioClipAsset clip = assets.Load<AudioClipAsset>(AssetPath.Project("Audio/Jump.wav"));
if (assets.TryGetArtifact(clip.persistentId, "audio-data", out AssetArtifactInfo? data))
    Console.WriteLine(data.absolutePath);
```

Runtime 只依赖 `IAssetArtifactLookup`，因此同一代码可在 Editor 的 `AssetPipeline` 与 Player 的 `AssetDatabase` 上解析 Artifact。Ogg、Opus 与 transcoding 不属于当前项目；未来格式由独立 importer/codec Plugin 提供。

