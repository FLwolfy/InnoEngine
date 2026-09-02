# Inno.Plugins

[Plugins 索引](README.md) · [Authoring](Inno.Plugins.Authoring.md) · [Runtime](../runtime/Inno.Runtime.md)

该 Player-safe project 只拥有当前 Plugin manifest contract。

## 公开 API

- `PluginManifest`：稳定 Plugin ID、显示名、依赖 ID、override 与 Project Setting contribution 的结构化文档。

Manifest 实现 `ISerializable` 并通过统一 SerializationRegistry 读写。它不列出 Component、Importer、Panel、Shader Node 等扩展类型；这些由 Attribute Registry 发现。Manifest 无 schema version、旧字段 alias 或 fallback reader。
