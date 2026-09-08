# Inno.Animation.Assets

[Animation 索引](README.md) · [Contract](Inno.Animation.md) · [Assets Pipeline](../assets/Inno.Assets.Pipeline.md)

`AnimationClipImporter` 自动发现 `.ianim` structured source，使用全局 `SerializationRegistry` 解码当前格式，调用 `AnimationClipAsset.Validate()`，并输出名为 `runtime` 的不可变 Artifact。导出走同一结构化序列化系统，不建立 JSON 旁路。

损坏 duration、乱序 keyframe/marker、重复 binding、混合 value kind 或空 event ID 会令 import 明确失败。Importer 是 authoring-only；Player closure 只包含 `Inno.Animation`、Runtime 与冻结 Artifact，不包含本项目。
