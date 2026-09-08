# 架构治理

[Wiki 首页](../README.md) · [完整项目架构 Overview](ENGINE_ARCHITECTURE_OVERVIEW.md) · [Identity、可恢复引用与热重载标准](IDENTITY_REFERENCE_RELOAD_STANDARD.md) · [当前已知问题](CURRENT_ISSUES.md)

本分类记录跨越多个程序集、无法准确归属到单个项目页的架构事实。它不替代各项目的 API
Wiki，也不把未来规划描述为当前能力。

## 文档

| 页面 | 内容 | 维护要求 |
| --- | --- | --- |
| [2026-09-08 实现交付与集中验收](ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md) | 全清单剩余实现、公开 API、资源 owner 与集中验收 | 当前唯一状态入口，实现与验证分开 |
| [2026-09-07—09-08 累积收口报告](ENGINE_CLOSURE_CONTINUATION_2026_09_07.md) | 之前 Source/Scene Recovery、Asset/Audio/Rendering 退休与 996 项基线 | 保留历史证据，不代替最新验收 |
| [2026-09-07 前轮收口报告](ENGINE_CLOSURE_ACCEPTANCE_2026_09_07.md) | 前轮实现与 742 项测试、发布证据 | 保留历史证据；当前状态以续轮报告为准 |
| [引擎统一收口实施与验收](ENGINE_CONSOLIDATION_IMPLEMENTATION.md) | 2026-09-07 批准的子系统、Execution、Diagnostics、Recovery、默认装配完整实施清单 | 逐项分别记录实现状态与实际验证结果 |
| [完整项目架构 Overview 与本体收口方案](ENGINE_ARCHITECTURE_OVERVIEW.md) | 概念分层、磁盘/Solution/Distribution 视图、生命周期、backend 政策与实施顺序 | 新系统立项、跨域引用或 Host composition 改变时必须先核对此页 |
| [Identity、可恢复引用与热重载强制标准](IDENTITY_REFERENCE_RELOAD_STANDARD.md) | 跨域 Identity、ImGui runtime ID payload、Missing 保留/恢复、History 和 GC unload barrier | 修改 Scripting、Plugins、Assets、Scene、Editor 引用或任何 collectible generation 时必须核对此页 |
| [当前已知问题](CURRENT_ISSUES.md) | 已确认缺陷、架构债务、验证缺口和整改顺序 | 问题修复时必须同步更新状态、证据与验证结果 |

## 优先级定义

- **P0**：已确认的运行正确性问题，或直接违反项目强制架构边界的问题。
- **P1**：阻碍可靠发布、可复现构建、跨平台交付或长期扩展的结构性问题。
- **P2**：当前可以工作，但会持续增加维护、诊断、性能或 API 使用成本的问题。

优先级不是兼容格式或持久化 schema。问题编号只用于代码审查、测试和文档之间的稳定引用。
