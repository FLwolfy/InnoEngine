# MaterialGraph 双资产路径残留（暂缓处理）

[问题索引](README.md) · [当前架构问题](../architecture/CURRENT_ISSUES.md) · [MaterialGraph](../render/Inno.Rendering.MaterialGraph.md)

状态：**已确认，按用户 2026-09-11 指示仅记录，本轮不处理。**

## 当前问题

当前界面以普通 `.imaterial` / `MaterialAsset` 的内嵌图为主要工作流，但源码仍存在另一条独立资产路线：

- `MaterialGraphAsset : MaterialAsset` 与单独的 document bytes。
- `MaterialGraphAssetImporter` 仍注册 `.imaterialgraph`。
- `MaterialGraphEvaluator` 仍保留以 `MaterialGraphAsset` 为入口的求值、提交重载。
- 脚本导出仍包含 `MaterialGraphAsset`；Panel 恢复状态时还按旧扩展名清理路径。

这与“一份 Material 对应一张图，不保留 legacy”的目标不一致。普通材质内嵌图的实现与独立图资产
并存，扩大了资产类型、Importer、持久化与测试维护范围。当前功能可运行不代表上述残留已经收口。

## 后续处理边界与验收

待用户要求处理时，只保留普通 Material 的内嵌图路线；同步删除独立资产类型、Importer、旧重载、
脚本导出、Panel 旧路径分支，并更新调用方、测试和文档。没有旧格式迁移器、兼容字段或隐藏 fallback。
检查 `MaterialAsset` 是否仍有真实继承需求，不能只为已移除的类型保持开放继承。

验收应覆盖普通 `.imaterial` 的打开、编辑、保存、重载及 Player 内容导出，确认运行时材质绑定与唯一
Shader IR 编译链不变，并检查仓库中不再存在独立 `.imaterialgraph` 的功能入口。

本次 Transform、VSync 与设备诊断修正不修改任何 MaterialGraph 实现，也不将此问题标记为完成。
