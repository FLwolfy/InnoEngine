# Inno.Tooling.Architecture

[Tooling 索引](README.md) · [Issues](../issues/README.md) · [整改规格](../issues/2026-08-31-architecture-remediation-master-plan.md)

退休 catch 的 AST 规则拒绝直接捕获 `RetirementPendingException` / `RetirementTimeoutException`（包括全限定名）。
应捕获 Exception，并用 Core `RetirementPendingException.Find` 检查整棵异常树后原样传播。
规则包含两个反例和一个合法 filter 正例；它是源码约束，不声称静态证明任意动态异常、类型别名或 native callback 行为。

Registry 派生实现不得直接调用 `OnCleanupFailed` 代替退休失败传播。源码 AST 检查会拒绝该绕过；统一清理应使用 `DisposeExtensions` 并让 TypeRegistry 封锁 generation。负向行为见[CLI 测试项目](Inno.Tooling.Architecture.Tests.md)。

该 executable 没有稳定 library API。它从包含 `InnoEngine.sln` 的根目录加载源码与 `.csproj` 图，并以非零退出码报告违反项。

## 检查范围

源码检查之外，工具通过 Roslyn 读取刚构建的 Debug 程序集元数据，递归检查 public/protected
基类、接口、返回值、参数、泛型约束与外层封闭泛型、数组、tuple、指针及 unmanaged function pointer 中的依赖。
它能识别类型别名和多行签名，不依赖名称正则。有效可见性同时考虑外层容器，internal/private 容器中的 public
成员不被误判为对外 API；private-protected 成员不形成程序集外扩展契约。对应正反例由真实编译 DLL 驱动的 CLI 测试覆盖。
Editor 公开签名中实际需要的项目不得标为 PrivateAssets=compile；Host/Shell 不得公开具体 Adapter。
Native 符号检查目前覆盖 BGFX、SDL3、MiniAudio；ImGui presentation 的边界仍由专项规则约束，
不宣称已经通过统一规则证明所有 ImGui 类型均不可见。

必须先构建 Solution，随后以 --no-build 运行审计；工具不替代编译，也不证明任意插件的动态线程行为。

- friend assembly、Obsolete、type forwarder、兼容字段和禁用实现名；
- global/implicit using、循环 ProjectReference 和 removed project；
- Core/Build/Runtime/Rendering/Audio/Native 依赖方向；
- Player dependency closure；
- Native 类型泄漏和 Engine 内部脚本 Log facade；
- tests 的 non-public reflection；
- 全部 test project 必须进入 solution，并位于 `tests/<domain>` 虚拟 Solution Folder；TestModule/TestAssembly/TestDependency 必须继续位于二级 `fixtures`；
- `Inno.Audio` 不得引用 Runtime、Scene、Editor、Platform、Native 或具体 backend；只有 MiniAudio adapter/toolchain/native/tests 可直接引用 native binding；
- Audio scripting 清单不得导出设备、native binding 或 backend，MiniAudio 实时适配源码不得持有托管 extension generation/reflection 对象；
- public/protected 多行英文 XML 的 summary/param/typeparam/returns/exception contract。

```text
dotnet build InnoEngine.sln -m:1 -p:UseSharedCompilation=false
dotnet run --project tools/Inno.Tooling.Architecture --no-build -- .
```

修复工具参数只用于机械展开/补全 XML，不改变领域行为；正常 CI 运行不使用修复参数。
