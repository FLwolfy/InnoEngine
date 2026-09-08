# Inno.Runtime.Generators

[Runtime 索引](README.md) · [Wiki 首页](../README.md) · [Contracts](Inno.Runtime.Contracts.md) · [Default](Inno.Engine.Default.md)

本项目是 Roslyn incremental generator，只作为 Analyzer 引用，不进入 Player 执行闭包。唯一公开实现入口是 `RuntimeSubsystemGenerator.Initialize`，由编译器调用，不是游戏 API。

同一发行程序集中的 `[RuntimeSubsystemRegistration("stable.id")]` 标记静态 factory 方法，参数是明确的 composition 类型，返回 `IRuntimeSubsystemFactory`。带 `[RuntimeSubsystemCatalog]` 的静态 partial 方法返回 `IReadOnlyList<IRuntimeSubsystemFactory>`，生成器按参数类型收集声明，按 ID 排序并输出直接 C# 调用与冻结列表。返回 factory 的 descriptor ID 还会被再次核对。

`INNORUN001` 拒绝空 catalog、重复 ID、错误 factory 返回边界和不支持的 catalog 形状。没有运行时反射，没有扫描任意未引用程序集，也没有 Plugin 类型中央名单。

新增引擎能力时需要：领域 contract/runtime、发行项目引用、发行项目中该领域的一个声明文件。无需修改 Shell、EditorHost、GamePlayerHost 或 generator 中的领域 switch。生成器测试覆盖新增声明自动进入 catalog、重复 ID 和无效声明。
