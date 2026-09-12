# 当前架构问题入口

[架构治理](README.md) · [Wiki 首页](../README.md) · [正式问题台账](../issues/2026-08-31-complete-issue-register.md)

2026-09-12 更新：[MaterialGraph 双资产路径清理](../issues/2026-09-11-material-graph-dual-asset-path.md)
已按后续用户授权完成；单一 Shader 创作链及尚未完成的验收见
[统一 Shader 实施记录](../issues/2026-09-11-unified-shader-implementation.md)。历史全引擎验收不能替代本次改动的验证。

本次 C01–C18 收口的当前状态与阻断项统一维护在
[2026-09-08 实现交付与集中验收](ENGINE_CLOSURE_IMPLEMENTATION_2026_09_08.md)；
[实施清单](ENGINE_CONSOLIDATION_IMPLEMENTATION.md)保留阶段记录。
最新实现已交付，自动验收为 48 项目 / 1021 passed / 0 failed / 0 skipped；无保留验收仍保留 GUI 权限阻断，以及一次未复现、尚未定位原因的 Plugin 移除测试失败记录。ImGui UI 公开面是既定设计，不是待整改项。
此前 2026-08-31 的问题编号和历史证据仍见[全量问题台账](../issues/2026-08-31-complete-issue-register.md)，不能用历史通过结果替代当前验收。

历史审查证据保存在[完整架构审查结论](../issues/2026-08-31-full-architecture-audit.md)，最终目标和实施顺序保存在
[全仓架构整改总方案](../issues/2026-08-31-architecture-remediation-master-plan.md)。

同日反馈的自动编译/旧代验证循环已修复：排除当前候选的 SourceMountsChanged 通知回声，票据/IDE 投影等待 GC 完成，Faulted 不再显示无限忙碌；同时移除 LifetimeScope 自有任务多余的完成观察回调。最终验收与新自动模式测试见上述当前报告文末。
