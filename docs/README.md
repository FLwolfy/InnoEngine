# InnoEngine Wiki

这里是 InnoEngine 的源码级 API Wiki。文档按 `src` 下的程序集边界组织；每个项目页同时说明职责、初始化方式、公开 API、常见工作流和易错点。

> 当前详细覆盖 Core、Assets、Rendering 与 Editor；Engine、Platform 与 Native 会沿用相同结构继续补充。

## 快速入口

| 分类 | 内容 | 状态 |
| --- | --- | --- |
| [Core](core/README.md) | 程序集、反射、序列化、框架、事件、协程、任务、数学、存储等基础设施 | 已完成 |
| [Assets](assets/README.md) | 多 Source Mount、统一资产链、本地 ZIP/Folder Plugin 与原子激活 | 已完成 |
| [Rendering](render/README.md) | 空渲染内核、开放 Shader/Technique/Material/Pipeline、统一 Graph/IR、BGFX 与 Editor GPU 合成 | 当前工作树实现完成，双平台最终实机验收见项目 CI |
| [Engine](engine/README.md) | Scene、Rendering 等运行时能力 | 部分完成 |
| [Editor](editor/README.md) | 编辑器交互契约、[Play Mode](editor/Inno.Editor.PlayMode.md)、Global/Settings、领域 feature、ImGui、Scripting 与宿主 | 已完成 |
| [Platform](platform/README.md) / Native | 窗口、图形后端与原生集成 | 部分完成 |

## 推荐阅读路线

如果想理解引擎启动与扩展发现，建议依次阅读：

1. [Inno.Core.Assemblies](core/Inno.Core.Assemblies.md)：活动程序集目录与事务式 Reload。
2. [Inno.Core.Reflection](core/Inno.Core.Reflection.md)：TypeCache、Stable Type ID 与通用 TypeRegistry。
3. [Inno.Core.Scripting](core/Inno.Core.Scripting.md)：每项目显式脚本 API 清单。
4. [Inno.Core.Serialization](core/Inno.Core.Serialization.md)：属性序列化与 Converter 扩展。
5. [Inno.Assets.Loader](assets/Inno.Assets.Loader.md)：Importer 发现、导入缓存与加载。
6. [Inno.Assets](assets/Inno.Assets.md)：应用层使用的资产系统门面。
7. [Inno.Assets.Plugins](assets/Inno.Assets.Plugins.md)：ZIP/Folder Plugin、安全校验与 Source Mount 原子激活。
8. [Rendering](render/README.md)：由 Project/Plugin 构建具体渲染模型的空内核。

如果只想开始写游戏脚本，通常先看 [Framework](core/Inno.Core.Framework.md)、[Mathematics](core/Inno.Core.Mathematics.md) 和 [Assets](assets/README.md) 即可。

## 文档约定

- 文中的 API 名称与当前源码一致，包括引擎已有的 Unity 风格小写属性，例如 `isInitialized`、`deltaTime`。
- “公开 API”包括 `public` 类型/成员，也包括面向派生类实现者的重要 `protected` 扩展点。
- 标为 `internal` 的实现只在解释运行机制确有必要时出现，不作为稳定调用契约。
- 示例默认使用 .NET 9、nullable enabled，并假定相关 Manager 已按页面说明初始化。
- Wiki 描述的是当前工作树；若源码行为和 Wiki 冲突，以源码为准，并应在同一变更中修正文档。

## 源码与 Wiki 的关系

Wiki 不替代公开 API 上的英文 XML 注释。XML 注释回答“这个成员是什么”，Wiki 重点回答“何时使用、如何组合、生命周期如何衔接，以及有哪些约束”。维护方法见根目录 [`AGENTS.md`](../AGENTS.md) 的 Wiki 章节。
