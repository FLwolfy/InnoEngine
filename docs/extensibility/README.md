# Extensibility API

[Wiki 首页](../README.md) · [Scripting](../scripting/README.md)

| 项目 | 职责 |
| --- | --- |
| [Inno.Extensibility.Modules](Inno.Extensibility.Modules.md) | shadow copy、collectible ALC、依赖闭包、candidate transaction 与 unload monitor |
| [Inno.Extensibility.Types](Inno.Extensibility.Types.md) | Stable Type ID、不可变 TypeCache snapshot 与通用 TypeRegistry |

所有 Attribute 扫描只发生在 candidate build。候选完整验证后在安全点原子 Activate；持久状态只保存 Stable ID 和中立数据，不跨 generation 长期保存 `Type`、实例或 delegate。
