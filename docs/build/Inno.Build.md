# Inno.Build

[Build 索引](README.md) · [Wiki 首页](../README.md) · [Runtime](../runtime/Inno.Runtime.md)

## 职责与边界

Build bounded context 拥有 Game/Plugin 构建编排、组合 generation 检查、流式 Content Pack、Support Pack 验证、staging 和原子提交。它引用 authoring 服务，但绝不引用 Editor；平台布局由 `IGameBuildTarget` 提供。

Game Build 在内容打包前把已验证 Support Pack 作为目标运行时交给 Script Compiler。脚本仍先经过当前裁剪 API 校验，再针对 Pack 中实际部署的引擎程序集编译；因此部署兼容性不再由 Editor 进程加载顺序推导的全局指纹决定，也不会到 Player 启动后才以紫色错误画面暴露二进制不兼容。

## 公开 API

| API | 语义 |
| --- | --- |
| `BuildProfile`, `BuildProfileStore`, `BuildTargetId` | 当前格式、可验证的构建配置与目标身份；窗口宽高只定义 Player 初始窗口，不定义内容 aspect |
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

Editor 第一次创建 Build Profile 时优先使用当前 active 且已有源路径的 Scene；没有可用 active Scene 时才回退到按路径排序的第一个已导入 Scene。此后启动 Scene、目标与初始窗口尺寸都以保存的 Build Profile 为准，不会在每次打开导出窗口时被默认值覆盖。游戏内容的参考分辨率与保持比例策略属于项目 `GamePresentationSettings`，由 Game View 和 Player 共用。

## 错误与生命周期

调用者拥有 cancellation 和 progress；`BuildPipeline` 使用注入服务但不拥有其生命周期。输出不能位于 Assets、Plugins 或 Library。损坏 Artifact、缺失 Support Pack、无 runtime assembly、目标返回越界路径都会在提交前失败。
