# Inno.Player

[Runtime 索引](README.md) · [Shell](Inno.Shell.md) · [Build](../build/README.md)

`Inno.Player` 是标准游戏产品的 Composition Root，不提供脚本稳定 API。`GamePlayerHost : Shell` 负责读取已冻结 runtime manifest、创建 EngineHost/RuntimeSession、装配中立 Runtime Subsystems 并加载 startup Scene。

Player 通过 `DefaultAdapterCatalog` 与 `AdapterSelection` 选择默认后端。Host 源码和公开/protected surface 不包含 SDL3、BGFX、MiniAudio、FileSystem 或 ImGui implementation 类型。

Player closure 必须保持 source-free：不得包含 Editor、Build、AssetPipeline、Scripting Compiler/Reload、Plugin Authoring 或 Toolchain。原生 Release 文件只由 Support Pack 放入最终 `native/` 目录。

项目 Settings 在 RuntimeSession 创建 AssetDatabase 后、领域子系统构造前初始化，并使用该数据库的
完整 Asset resolver context。配置中的冷资产引用因此落在正确的 Session identity domain；不再先创建
没有 resolver 的 Settings。默认 Audio 配置通过定义明确的 Settings 契约读取，不使用缺失时静默 new 默认值的旁路。

`--smoke-frames` 使用 Shell 的有界 run loop，并输出最终 rendering statistics；普通启动运行到 application 或 primary window 请求退出。
