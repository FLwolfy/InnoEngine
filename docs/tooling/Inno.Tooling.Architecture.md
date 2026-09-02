# Inno.Tooling.Architecture

[Tooling 索引](README.md) · [Issues](../issues/README.md) · [整改规格](../issues/2026-08-31-architecture-remediation-master-plan.md)

该 executable 没有稳定 library API。它从包含 `InnoEngine.sln` 的根目录加载源码与 `.csproj` 图，并以非零退出码报告违反项。

## 检查范围

- friend assembly、Obsolete、type forwarder、兼容字段和禁用实现名；
- global/implicit using、循环 ProjectReference 和 removed project；
- Core/Build/Runtime/Rendering/Native 依赖方向；
- Player dependency closure；
- Native 类型泄漏和 Engine 内部脚本 Log facade；
- tests 的 non-public reflection；
- public/protected 多行英文 XML 的 summary/param/typeparam/returns/exception contract。

```text
dotnet run --project tools/Inno.Tooling.Architecture -- .
```

修复工具参数只用于机械展开/补全 XML，不改变领域行为；正常 CI 运行不使用修复参数。
