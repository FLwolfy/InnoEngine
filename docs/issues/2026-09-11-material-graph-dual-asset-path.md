# MaterialGraph 双资产路径清理记录

[问题索引](README.md) · [统一 Shader 实施记录](2026-09-11-unified-shader-implementation.md)

状态：原先暂缓的删除已得到用户授权，代码及当前资产残留已清理；统一 Shader 的整体验收单独跟踪。

已删除独立 MaterialGraph 类型、importer、求值器、Panel、项目引用、脚本导出和测试项目。当前材质的旧内嵌图 metadata 已移除，Shader 引用与参数覆盖保持不变。过时的产品 API 文档已删除；历史架构审计报告保留为历史证据，不是当前 API。

当前模型只有：Shader 图定义 GPU 程序，Material 选择 Shader 并提供参数。没有旧格式迁移器、别名或双格式加载分支。

旧完整源码 Shader API 的类型和编译重载也已从生产及测试代码移除。完成与否以统一实施记录中的构建、保存/历史、UI 和 GPU 验收结果为准，不以本项清理替代总体验收。
