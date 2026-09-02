# Inno.Rendering.Scene

[Rendering 索引](README.md) · [Scene](../scene/Inno.Scene.md) · [Runtime Rendering](Inno.Rendering.Runtime.md)

该 project 是可选的 Scene/Rendering 集成层，使 backend-neutral `Inno.Rendering` 不需要引用 Scene 世界观。

公开 `SceneRenderContent` 提供当前 SceneWorld 与 render request/content scope 的组合入口。具体 2D、3D、camera、light 或 pipeline 不属于本项目，必须由 Project/Plugin extension 定义。
