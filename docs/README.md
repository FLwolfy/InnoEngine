# InnoEngine Wiki

本文档按当前程序集与 bounded context 组织。公开契约以源码和英文 XML 为准；Wiki 解释 owner、依赖方向、组合方式和生命周期。历史项目、旧 namespace 和兼容入口不在 Wiki 中保留。

## 分类入口

| 分类 | 稳定职责 |
| --- | --- |
| [Core](core/README.md) | 无业务世界观的基础设施 |
| [Extensibility](extensibility/README.md) | collectible module generation、Stable Type ID 与 Registry snapshot |
| [Scripting](scripting/README.md) | 脚本 API、编译与原子 reload |
| [Assets](assets/README.md) | Player-safe runtime assets 与 authoring pipeline |
| [Audio](audio/README.md) | 后端中立播放/Mixer 契约、Runtime、资产、MiniAudio 与 Scene 集成 |
| [Plugins](plugins/README.md) | Plugin manifest、安装源、只读 mount 与候选激活 |
| [Scene](scene/README.md) | SceneWorld、GameBehavior、GameSystem、Scene/Prefab asset integration |
| [Rendering](render/README.md) | 后端中立 Rendering、目标资产、BGFX 与 ShaderGraph |
| [Platform](platform/README.md) | 中立窗口契约与 SDL3 adapter |
| [Runtime](runtime/README.md) | EngineHost、RuntimeSession 与 Player composition |
| [Editor](editor/README.md) | Editor feature、Panel、Play Mode、Diagnostics 与 Export UI |
| [Build](build/README.md) | Build Pipeline、平台 target、Support Pack 与 toolchain |
| [Native](native/README.md) | 原生绑定与动态库加载 |
| [Architecture Tooling](tooling/README.md) | 可执行架构规则 |
| [Issues](issues/README.md) | 唯一问题台账、审查记录与整改规格 |

## 核心依赖方向

```text
Application / Player / Build CLI
        ↓
Editor / Build / Runtime
        ↓
Scripting / Plugins / Assets / Audio / Scene / Rendering / Platform
        ↓
Extensibility / Core / Native adapters
```

Core 不引用业务领域；Build 不引用 Editor；Runtime 不引用 Build/Editor；Player closure 不包含 Compiler、authoring pipeline 或 toolchain。违反关系由 `Inno.Tooling.Architecture` 阻止。

## 当前格式与状态

- Project Settings、Editor Settings、Build Profile、Plugin Manifest、Catalog 与 Artifact 只支持当前源码格式。
- `Assets` 是唯一可写创作源；`Plugins` 是只读安装源；`Library` 可完全重建。
- API 变更必须同步源码 XML、所属项目页和索引。
- 当前问题状态只在[全量问题台账](issues/2026-08-31-complete-issue-register.md)维护。
