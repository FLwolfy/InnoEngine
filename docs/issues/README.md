# Issues 与审查记录

[Wiki 首页](../README.md) · [架构治理](../architecture/README.md)

本目录保存历史问题编号、审查原文、设计决策与整改证据。
当前 C01–C18 收口状态见[2026-09-07 续轮报告](../architecture/ENGINE_CLOSURE_CONTINUATION_2026_09_07.md)；
[架构治理](../architecture/CURRENT_ISSUES.md)统一提供当前与历史入口，不复制独立状态表。

## 审查记录

| 日期 | 页面 | 范围 |
| --- | --- | --- |
| 2026-09-12 | [Shader 草稿保存与 Console 布局](2026-09-12-shader-drafts-and-console.md) | 显式 Save 取代自动保存；修复未提交事务、资产身份、阶段删除及详情标签裁剪；242 项通过 |
| 2026-09-11 | [统一 Shader 实施记录](2026-09-11-unified-shader-implementation.md) | 新批准方案；基础契约与导入设置已实施，完整替换及验收未完成 |
| 2026-09-11 | [MaterialGraph 双资产路径残留](2026-09-11-material-graph-dual-asset-path.md) | 已授权并删除代码及当前资产残留；统一 Shader 验收独立跟踪 |
| 2026-09-08 | [累积收口报告](../architecture/ENGINE_CLOSURE_CONTINUATION_2026_09_07.md) | Source/Scene Recovery、Asset/Audio/Rendering Pending 退休、837 项回归和持续收口目标 |
| 2026-09-07 | [前轮收口报告](../architecture/ENGINE_CLOSURE_ACCEPTANCE_2026_09_07.md) | 前轮源码修复、742 项本地回归与发布验证 |
| 2026-08-31 | [完整架构审查结论](2026-08-31-full-architecture-audit.md) | Core、Assets、Plugin、Scripting、Play Mode、Editor、Rendering、Native、Game Export、Player、测试与整改顺序 |
| 2026-08-31 | [Build 边界、分层与 API 决策](2026-08-31-build-boundary-and-layering-decisions.md) | Build 命名、前后端分离、耦合判断、API 和程序集决策 |
| 2026-08-31 | [全量问题台账](2026-08-31-complete-issue-register.md) | 56 项问题、最终 owner、关闭证据与不可妥协约束 |
| 2026-08-31 | [全仓整改总方案](2026-08-31-architecture-remediation-master-plan.md) | 最终项目结构、依赖、公开 API、实施阶段、测试和验收标准 |
