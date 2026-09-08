# Animation API

[Wiki 首页](../README.md) · [Runtime](../runtime/README.md) · [Assets](../assets/README.md)

Animation 是本体的后端中立采样机制；Scene property、Transform、Sprite、骨骼、Timeline 与 Visual Novel 编排不是本体世界观。

| 项目 | 职责 |
| --- | --- |
| [Inno.Animation](Inno.Animation.md) | Clip、track、value、binding、播放句柄、service 与 façade |
| [Inno.Animation.Runtime](Inno.Animation.Runtime.md) | 每 Session 播放、采样、分层混合、marker 与 Feature lifecycle |
| [Inno.Animation.Assets](Inno.Animation.Assets.md) | 当前 `.ianim` structured source importer/exporter |
