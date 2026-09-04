# Inno.Build

[Build 索引](README.md) · [Wiki 首页](../README.md) · [Runtime](../runtime/Inno.Runtime.md)

## 职责与边界

Build bounded context 拥有 Game/Plugin 构建编排、组合 generation 检查、流式 Content Pack、Support Pack 验证、staging 和原子提交。它引用 authoring 服务，但绝不引用 Editor；平台布局由 `IGameBuildTarget` 提供。

Game Build 在内容打包前把已验证 Support Pack 作为目标运行时交给 Script Compiler。脚本仍先经过当前裁剪 API 校验，再针对 Pack 中实际部署的引擎程序集编译；因此部署兼容性不再由 Editor 进程加载顺序推导的全局指纹决定，也不会到 Player 启动后才以紫色错误画面暴露二进制不兼容。

## 公开 API

| API | 语义 |
| --- | --- |
| `BuildSettings`, `BuildSettingsStore` | 项目拥有的 Game/Plugin 导出默认值；使用当前 Inno Serialization 原子保存为 `Settings.Build.inno` |
| `BuildProfile`, `BuildProfileStore`, `BuildTargetId` | 一次 Game 构建所需的可验证 profile 与目标身份；显式 profile 文件可供 headless one-off 构建使用 |
| `GameBuildRequest`, `PluginBuildRequest` | 一次不可变构建请求 |
| `BuildProgress`, `BuildDiagnostic`, `BuildDiagnosticSeverity`, `BuildResult` | 进度、结构化诊断与最终结果 |
| `BuildPipeline` | Game/Plugin 的最小异步入口 |
| `IGameBuildTarget` | 真正可替换的平台目标 contract |
| `GameBuildContentContext`, `GameBuildPackageContext` | 平台目标获得的隔离 staging context |
| `PlayerSupportPackCatalog` | 验证并解析部署 closure |

Content writer、`.iplugin` archive writer、snapshot fingerprint、script stage、staging transaction 与 player composer 全部 internal。

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

`Settings.Build.inno` 保存团队可版本控制的导出默认值；文件不存在时，composition root 以项目名、host target 和按路径排序的第一个已导入且可部署 Scene 建立隔离默认值，`~` authoring sample 中的 Scene 不会被自动选为 Startup Scene。Editor 的 Settings Apply 才会持久化该文件。每次打开导出 modal 都重新复制这些默认值，modal 内修改只属于本次请求，绝不回写 `Settings.Build.inno`。Game Application ID 与 Plugin ID 不是 Build 默认值，而是直接取 `Settings.Project.inno` 中的当前 Project ID；`BuildProfile` 仅保存 one-off 构建参数，加载后也会绑定当前 Project ID。

`Settings.Editor.inno`、`Settings.Project.inno` 和 `Settings.Build.inno` 不合并：它们分别属于本机 Editor 偏好、runtime 项目协议和 authoring/build 默认值。只有 `Settings.Project.inno` 进入 Player；Build Settings 和 Editor Settings 都不会进入 runtime closure。游戏内容的参考分辨率与保持比例策略属于项目 `GamePresentationSettings`，由 Game View 和 Player 共用。

## 错误与生命周期

调用者拥有 cancellation 和 progress；`BuildPipeline` 使用注入服务但不拥有其生命周期。输出不能位于 Assets、Plugins 或 Library。损坏 Artifact、缺失 Support Pack、无 runtime assembly、目标返回越界路径都会在提交前失败。Startup Scene 必须是已导入且可部署的 `SceneAsset`；位于任意 `~` 目录时会以 authoring-only 错误明确拒绝。
