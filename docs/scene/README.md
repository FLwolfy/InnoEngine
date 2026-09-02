# Scene API

[Wiki 首页](../README.md) · [Runtime](../runtime/README.md) · [Rendering](../render/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Scene](Inno.Scene.md) | SceneWorld、GameScene、GameObject、GameBehavior、GameSystem、serialization/state transfer |
| [Inno.Scene.Assets](Inno.Scene.Assets.md) | Scene/Prefab importer 与 common Asset Pipeline integration |

Scene 状态由 `SceneWorld` 实例持有。Unity 风格 `SceneManager` 只解析当前 RuntimeSession。具有 enable 与帧生命周期语义的 Component 统一直接继承 `GameBehavior`；Scene 级序列化协调对象直接继承 `GameSystem`。
