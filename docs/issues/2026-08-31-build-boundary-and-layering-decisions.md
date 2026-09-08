# Build 边界、分层与 API 决策记录

[Issues 索引](README.md) · [完整架构审查](2026-08-31-full-architecture-audit.md) · [整改总方案](2026-08-31-architecture-remediation-master-plan.md)

本文完整记录 2026-08-31 在全仓架构审查之后，对 Build 命名、程序集归属、前后端分离、迁移方式、API 形态和封装原则作出的正式决定。本文是设计决策，不是对当前实现已经完成的声明。

## 原始问题一：导出系统应放在哪里

> 你认为导出系统应该独立放到 Inno.Build.XXX 中吗？还是在 Inno.Core.Export？还是 Inno.Export.XXX 之类的？

### 完整结论

导出后端必须独立放在 `Inno.Build.*`，不能放入 `Inno.Core.Export`，也不应建立 `Inno.Export.*` 领域。

原因如下：

1. `Export` 是 Editor 中的用户动作，不是后端领域。相同 Build 能力还要被 CLI、CI 和未来的自动化调用。
2. Core 只能包含不认识 Assets、Scene、Plugin、Editor、Player 和具体平台的通用机制。Game Build 必然编排这些领域，因此进入 Core 会造成依赖倒置。
3. Build 是高层用例层，允许在 Composition Root 下游组合 Assets Pipeline、Scripting Compiler、Plugin Authoring、Player Support Pack 和平台打包器。
4. Editor 只负责 File 菜单、Modal、进度、取消和结果展示；不能拥有 `dotnet publish`、ZIP、Artifact 打包或平台布局算法。
5. Player 不能引用 Build。Build 生产 Player，Player 只消费 runtime manifest 和 Artifact Bundle。

最终命名采用：

- `Inno.Build`：公共 Build 请求、结果、Profile 与内部流水线。
- `Inno.Build.Platform.MacOS`：macOS 平台最终打包。
- `Inno.Build.Platform.Windows`：Windows 平台最终打包。
- `Inno.Build.Cli`：无 Editor 的命令行入口。
- `Inno.Build.SupportPacks`：引擎开发阶段生成不可变 Player 支持包。
- `Inno.Build.Toolchains.*`：构建 SDL、BGFX、cimgui 等引擎依赖的离线工具。

Build 的 Content、Scripting、Player Composition 和 Plugin Packaging 是同一 bounded context 内的内部阶段，不为机械分层拆成多个程序集。只有真实可替换的平台目标通过最小 public contract 跨程序集。

## 原始问题二：整改的弊端与当前前后端分离程度

> 你认为当前你提到的整改有什么弊端吗？会增加耦合性还是降低耦合性？我当前项目前后端分离成功吗？还是全都杂糅了（不用尊重我，直接说点子上）

### 完整结论

当前架构不是“全都杂糅”，但也不能称为完整成功的前后端分离。准确结论是：宏观分离基本成功，微观用例分离只完成了一半。

已经成功的部分：

- Runtime 与 Editor 使用独立程序集。
- Rendering 已经形成后端中立 Core 和 BGFX Adapter 的设计方向。
- Assets、Scene、Scripting 和 Editor Feature 已经具备独立项目。
- Editor Application 基本承担 Composition Root。
- Plugin、Editor Extension 和 Rendering Extension 已采用 Attribute、Stable ID、Candidate 和原子切换思想。

仍然杂糅的部分：

- `Inno.Editor.Exporting` 同时拥有 UI、文件系统、进程启动、脚本编译协调、Artifact 导出和 Player 发布。
- `Inno.Editor.Scripting` 同时拥有 Roslyn 编译器、API 生成、缓存、热重载、Editor 状态和 Modal。
- Logging 的 Session Policy 和数据模型位于 Panel 项目中。
- `Inno.Core.Framework` 实际上是引用 Assets、Plugins、Settings 和 Assembly 系统的高层 Host。
- `Inno.Platform` 直接引用 SDL3，因此名称是抽象，程序集却是实现。
- Assets、Scene 与 Rendering 的若干边界依赖 `InternalsVisibleTo`，说明真实 owner 与项目拆分不一致。

整改本身会带来短期代价：

- 大量 namespace、项目引用和调用方需要同步修改。
- 完整 XML 文档和架构检查会显著增加初次整理工作量。
- 实例 Host 会迫使生命周期和依赖顺序从静态初始化变成显式构造。
- Runtime-safe 与 Authoring-only 程序集分开后，需要定义少量真正稳定的不可变协议。
- 测试不能再依赖 friend assembly，必须通过正式生产边界验证。

这些代价不会增加最终耦合。只要不滥建 `Abstractions`、不公开内部 Pipeline Stage、不用 Service Locator 替代构造依赖，最终耦合会降低。高层 Build 和 Application 具有较高 fan-out 是 Composition Root 的正常特征；领域项目之间的双向、隐式和 friend coupling 才是需要消除的耦合。

## 锁定的架构选择

### API 形态

采用“静态脚本门面 + 实例 Host”：

- 用户脚本继续使用 `Log`、`Time`、`Input`、`SceneManager` 等 Unity 风格入口。
- 真正状态由 `EngineHost` 和 `RuntimeSession` 持有。
- 引擎、Editor 和 Build 内部必须显式依赖实例服务。
- 静态门面只解析当前脚本执行上下文，不拥有进程单例。

### 迁移方式

采用分阶段硬切：

- 每阶段修改全部调用方并保持全仓可构建。
- 不保留旧 API wrapper、旧 namespace façade、`Obsolete` 类型或 fallback reader。
- 当前 Project 数据随源码直接更新，派生缓存重新生成。

### 程序集粒度

采用平衡领域边界：

- 平台、后端、运行时裁剪、Authoring/Runtime 和真实扩展点可以独立成程序集。
- 紧密协作的实现放在同一程序集的功能目录。
- 如果两个程序集需要大量互访 internal，优先移动所有权或合并，而不是扩大 public API。

## 最终强制原则

- 完全禁止 `InternalsVisibleTo`。
- 完全禁止 Legacy、Compatibility、Migration、Former、Deprecated 正常运行路径。
- 不为保留当前 API 作设计妥协。
- 类型默认 `internal`；测试需要不是公开理由。
- Attribute 驱动发现、Stable ID、候选验证、原子切换和显式 Scripting API 清单必须保留。
- Public API 只表达稳定领域能力，不暴露 UI、进程、文件布局或内部 Stage。
