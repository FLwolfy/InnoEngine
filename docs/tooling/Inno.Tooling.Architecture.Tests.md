# Inno.Tooling.Architecture.Tests

[Tooling 索引](README.md) · [架构工具](Inno.Tooling.Architecture.md) · [Wiki 首页](../README.md)

## 职责与边界

位于真实 `tests/tooling/Inno.Tooling.Architecture.Tests` 和相同 Solution Folder，验证实际架构 CLI 对错误源码的拒绝行为。它不是 Runtime/Player 依赖，也不提供游戏 API。

项目对 Architecture Tool 使用 `ReferenceOutputAssembly="false"` 构建依赖；测试启动工具进程，不引用其 internal 类、不使用 IVT 或反射测试后门。

## 验证协议

`ArchitectureCliTests.CliValidatesSourceContracts` 是公开 xUnit theory 入口。每次在临时目录建立隔离 solution，先验证其已知的三项“缺少产品 composition 项目”基线，再写入单项源码验证额外违规是否符合预期：

- global using。
- Player 泄露具体 Bgfx backend。
- 业务代码手工拼接 Asset resolver context。
- 非 native API 暴露 Ma 类型。
- 用 `OnCleanupFailed` 诊断回调吞掉资源退休失败。
- 直接 catch Pending / 全限定 Timeout，遗漏包装异常树。
- 合法 `Exception` + `RetirementPendingException.Find` filter 不新增违规。

该项目没有 protected 扩展契约。进程参数与输出均经正式 CLI；临时目录在 finally 清理。坏例不进入真实产品源码，也不会禁用原架构规则。

## 编译符号矩阵

`ArchitectureSymbolTests` 使用与工具相同的 Roslyn package，在临时 fixture 中编译真正的 dependency/product DLL，
放入对应项目的 Debug 输出位置，再启动正式 CLI。源码只在编译内存中存在，符号断言不能靠文本扫描碰巧通过。
没有引用工具 internal 类型、IVT、反射穿透或生产后门；不把这些故意缺少完整产品的 fixture 当作可运行引擎。

- 字段、属性、protected/protected-internal 方法、事件、delegate、基类和接口。
- 类型/方法 generic constraints、嵌套公开类型、数组、Task/集合嵌套、tuple。
- unsafe pointer、unmanaged function pointer 参数与返回值、封闭泛型外层中的 nested type。
- private、private-protected、internal 容器与 private nested type 的正向允许用例。
- BGFX、BGFX Tools、SDL3、MiniAudio 的真实 assembly identity，避免改类型名称或 alias 绕过规则。
- Shell/Player/Editor 的具体 Adapter 暴露与中立 contract 正向允许。
- Editor ProjectReference 的真实公开依赖、`PrivateAssets="compile"` 与有效可见性。
- SDL3 consumer 规则使用当前名称拼写；只允许对应 Platform/ImGui presentation、Native、toolchain 和 tests。

这组矩阵验证静态编译边界，不证明任意动态 callback、反射生成、原生函数入口或 GC 行为安全。
ImGui 原生 enum/pointer 的完整公开边界重构仍是实施清单 C17 的未关闭内容，不能用这些测试掩盖该缺口。

## 工作流

```sh
dotnet build InnoEngine.sln
dotnet test tests/tooling/Inno.Tooling.Architecture.Tests --no-build --no-restore
dotnet run --project tools/Inno.Tooling.Architecture --no-build
```

当前工具的符号检查要求 Debug solution 产物；负向源码 fixture 故意不伪造整个产品。测试不能替代完整解决方案归类检查、真实编译符号审计或运行时 GC/Recovery 验证。VSTest 需要本地进程通信权限。
