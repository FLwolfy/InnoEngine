# InnoEngine Wiki

本文档按当前程序集与 bounded context 组织。公开契约以源码和英文 XML 为准；Wiki 解释 owner、依赖方向、组合方式和生命周期。历史项目、旧 namespace 和兼容入口不在 Wiki 中保留。

## 分类入口

| 分类 | 稳定职责 |
| --- | --- |
| [Core](core/README.md) | 无业务世界观的基础设施 |
| [Extensibility](extensibility/README.md) | collectible module generation、Stable Type ID 与 Registry snapshot |
| [Scripting](scripting/README.md) | 脚本 API、编译与原子 reload |
| [Assets](assets/README.md) | Player-safe runtime assets 与 authoring pipeline |
| [References](references/README.md) | 跨领域持久引用、Missing 与恢复事务 |
| [Input](input/README.md) | 每 Session 的物理输入快照、脚本 façade 与 SDL3 adapter |
| [Storage](storage/README.md) | 沙箱化应用持久数据契约、Runtime Subsystem 与文件系统 adapter |
| [Animation](animation/README.md) | 后端中立 Clip、采样、混合、事件、资产与 Runtime Subsystem |
| [Audio](audio/README.md) | 后端中立播放/Mixer 契约、Runtime、资产与 MiniAudio adapter |
| [Plugins](plugins/README.md) | Plugin manifest、安装源、只读 mount 与候选激活 |
| [Scene](scene/README.md) | SceneWorld、GameBehavior、GameSystem、Scene/Prefab asset integration |
| [Rendering](render/README.md) | 后端中立 Rendering、目标资产、BGFX 与实施中的统一 Shader 创作层 |
| [Platform](platform/README.md) | 中立窗口契约与 SDL3 adapter |
| [Runtime](runtime/README.md) | Subsystem Contracts、声明生成器、默认装配、EngineHost、RuntimeSession 与 Player |
| [Editor](editor/README.md) | Editor feature、Panel、Play Mode、Diagnostics 与 Export UI |
| [Build](build/README.md) | Build Pipeline、平台 target、Support Pack 与 toolchain |
| [Native](native/README.md) | 原生绑定与动态库加载 |
| [Architecture](architecture/README.md) | Foundation、Content/Services/Runtime、Adapter、Composition 的边界，以及 Identity、Missing、Undo/Redo 与 GC-safe reload 标准 |
| [Architecture Tooling](tooling/README.md) | 可执行架构规则 |
| [Issues](issues/README.md) | 唯一问题台账、审查记录与整改规格 |

## 核心依赖方向

```text
EditorHost : Shell / GamePlayerHost : Shell / Build CLI
        ↓ compose through neutral catalogs
Default Adapter Implementations / Bundled Plugins
        ↓
Content / Services / Runtime
        ↓
Foundation (Extensibility / Core / Scripting API)

Native Bindings ← only Adapters / Toolchains / native tests
```

Core 不引用业务领域；Build 不引用 Editor；Runtime 不引用 Build/Editor；Player closure 不包含 Compiler、authoring pipeline 或 toolchain。违反关系由 `Inno.Tooling.Architecture` 阻止。引擎长期分层与新系统归属以
[完整项目架构 Overview 与本体收口方案](architecture/ENGINE_ARCHITECTURE_OVERVIEW.md)为准。
所有跨域 live object 索引、可恢复引用、Missing、Undo/Redo 与 collectible generation 的完成语义以
[Identity、可恢复引用与热重载强制标准](architecture/IDENTITY_REFERENCE_RELOAD_STANDARD.md)为准。
本轮代码、公开边界和逐项验证见[统一收口实施记录](architecture/ENGINE_CONSOLIDATION_IMPLEMENTATION.md)；
新增 Core.Execution、Runtime.Contracts、Runtime.Generators 和 Engine.Default 均有独立项目页。
最新本轮结果见[2026-09-08 实现交付与集中验收](architecture/ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md)；
新增 [Architecture CLI 测试项目](tooling/Inno.Tooling.Architecture.Tests.md)已纳入 `tests/tooling`、Solution 和项目文档。

## 当前格式与状态

新增 [Inno.Editor.Annotations](editor/Inno.Editor.Annotations.md) 已包含独立项目页与 Editor 索引；展示标注不再归属 Core.Serialization。
新增 [Inno.Rendering.Shaders](render/Inno.Rendering.Shaders.md) 已包含独立项目页与 Rendering 索引；
当前实现源码接口、多实现快照、节点降低与 typed stage/资源/分支/循环，完整 Shader 图替换的未完成项单独记录，不以单元测试或原生编译通过代替产品验收。
新增 [Shader Editor](editor/Inno.Editor.Panel.ShaderEditor.md) 与
[内置 Shader 离线工具](build/Inno.Build.Toolchains.Bgfx.Shaders.md) 已有项目页和分类索引。
图资产替换、自动保存、原生启动记录以及尚未通过的验收见[当前 Shader 检查点](issues/2026-09-11-unified-shader-implementation.md)。

- Project Settings、Editor Settings、Build Profile、Plugin Manifest、Catalog 与 Artifact 只支持当前源码格式。
- `Assets` 是唯一可写创作源；`Plugins` 是只读安装源；`Library` 可完全重建。
- API 变更必须同步源码 XML、所属项目页和索引。
- 当前 C01–C18 收口状态在[最新验收报告](architecture/ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md)维护；此前编号和历史证据保留于[2026-08-31 台账](issues/2026-08-31-complete-issue-register.md)。
