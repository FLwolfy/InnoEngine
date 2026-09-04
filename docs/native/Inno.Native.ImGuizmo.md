# Inno.Native.ImGuizmo

[Native 索引](README.md) · [Editor Scene](../editor/Inno.Editor.Scene.md)

该项目提供 cimguizmo binding，公开入口为 `ImGuizmoConfig` 及 generated gizmo API。它属于 Editor tool dependency，不属于 Scene 领域或 Player runtime。

Native library 必须由明确 toolchain 产物提供；初始化失败不能通过禁用 gizmo 的静默 fallback 掩盖。
