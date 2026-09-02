# Inno.Build

[Build 索引](README.md) · [Wiki 首页](../README.md) · [Runtime](../runtime/Inno.Runtime.md)

## 职责与边界

Build bounded context 拥有 Game/Plugin 构建编排、组合 generation 检查、流式 Content Pack、Support Pack 验证、staging 和原子提交。它引用 authoring 服务，但绝不引用 Editor；平台布局由 `IGameBuildTarget` 提供。

## 公开 API

| API | 语义 |
| --- | --- |
| `BuildProfile`, `BuildProfileStore`, `BuildTargetId` | 当前格式、可验证的构建配置与目标身份 |
| `GameBuildRequest`, `PluginBuildRequest` | 一次不可变构建请求 |
| `BuildProgress`, `BuildDiagnostic`, `BuildDiagnosticSeverity`, `BuildResult` | 进度、结构化诊断与最终结果 |
| `BuildPipeline` | Game/Plugin 的最小异步入口 |
| `IGameBuildTarget` | 真正可替换的平台目标 contract |
| `GameBuildContentContext`, `GameBuildPackageContext` | 平台目标获得的隔离 staging context |
| `PlayerSupportPackCatalog` | 验证并解析部署 closure |

Content writer、Plugin ZIP writer、snapshot fingerprint、script stage、staging transaction 与 player composer 全部 internal。

## 工作流

```csharp
BuildResult result = await pipeline.BuildGameAsync(
    new GameBuildRequest
    {
        profile = profile,
        outputDirectory = outputDirectory
    },
    progress,
    cancellationToken);
```

构建开始后会捕获 Assets/Plugins/Settings revision 与 Serialization generation。任一代际变化、取消或 stage 失败都会清理 staging，不覆盖已提交产品。

## 错误与生命周期

调用者拥有 cancellation 和 progress；`BuildPipeline` 使用注入服务但不拥有其生命周期。输出不能位于 Assets、Plugins 或 Library。损坏 Artifact、缺失 Support Pack、无 runtime assembly、目标返回越界路径都会在提交前失败。
