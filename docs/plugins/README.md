# Plugins API

[Wiki 首页](../README.md) · [Assets](../assets/README.md) · [Build](../build/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Plugins](Inno.Plugins.md) | Player-safe `PluginManifest` 当前格式 |
| [Inno.Plugins.Authoring](Inno.Plugins.Authoring.md) | ZIP/folder discovery、安全验证、依赖图、只读 mount、watcher 与原子 activation |

Project 根目录的 `Plugins` 与 `Assets` 平级。用户不创建 Plugin Definition；File 菜单的 `Export as Plugin` 自动生成 manifest。ZIP 与 folder 经过相同验证且安装内容只读。
