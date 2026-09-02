# Inno.Runtime

[Runtime 索引](README.md) · [Player](Inno.Player.md) · [Scene](../scene/Inno.Scene.md) · [Assets](../assets/Inno.Assets.md)

`Inno.Runtime` 是 Editor Play Mode 与独立 Player 共用的实例化执行宿主。它拥有 Host/Session 生命周期、脚本执行上下文和部署清单，但不拥有窗口、图形后端、Build 或 Editor UI。

## 所有权模型

`EngineHost` 持有可跨 Session 共享但仍按 Host 隔离的 Module、Type、Serialization、Logging 和 Diagnostics 服务。`RuntimeSession` 持有 SceneWorld、Identity、Job、Coroutine、Event、Clock、Session Log，以及可选的只读 `AssetDatabase`。一个 Host 可以同时创建多个互不污染的 Edit、Play 或 Player Session。

```csharp
using EngineHost host = new EngineHostBuilder()
    .UseMetadataCache(metadataCacheDirectory)
    .Build();

using RuntimeSession play = host.CreateSession(new RuntimeSessionOptions
{
    kind = RuntimeSessionKind.Play,
    applicationId = "sample.game",
    persistentDataDirectory = Path.Combine(userDataRoot, "sample.game")
});

play.Tick(totalTime, deltaTime);
```

`RuntimeSessionOptions.persistentDataDirectory` 的最后一个路径段必须严格等于 `applicationId`。`Player` Session 还必须提供已经物化的 `runtimeContentDirectory`；Edit/Play 可以由 Editor 组合 authoring 资产服务。

## 公开 API

| API | 作用 |
| --- | --- |
| `EngineHostBuilder` | 配置 Host metadata cache 并构建实例。 |
| `EngineHost` | 拥有应用级实例服务并创建隔离 Session。 |
| `RuntimeSessionOptions` | 定义 Session 角色、持久目录、运行内容、固定步长与 Job 策略。 |
| `RuntimeSession` | 暴露只读 Session 状态、SceneWorld、EventDispatcher、可选 AssetDatabase、执行作用域与 `Tick`。 |
| `RuntimeSessionKind` | 区分 `Edit`、`Play` 和 `Player` 所有权语义。 |
| `GameRuntimeManifest` | 描述当前 Player 的应用 ID、产品名、启动 Scene、窗口和 Plugin 设置贡献。 |
| `GameRuntimePlugin` | 保存依赖有序的中立 Plugin 设置贡献，不保存 Plugin `Type`、实例或 delegate。 |

## 脚本执行上下文

`Time`、`Input`、`SceneManager`、`Log`、`Assets` 和 `Settings` 等 Unity 风格门面只解析当前
Session 的执行作用域。`RuntimeSession.EnterExecutionScope()` 绑定 Runtime、Scene、Log 与可选
`AssetDatabase`；Player Composition Root 同时绑定它拥有的 `ProjectSettingsStore`。引擎实例服务
不调用这些门面。无活动 Session、Scope 乱序释放或 Session 已 Dispose 时都会明确失败，因此
并行 Session 不共享静态可变状态。

## 部署内容

Player 的 Runtime Session 使用 `AssetDatabase` 读取物化后的 Catalog 和 Artifact Bundle，不扫描 Source Mount、不运行 Importer，也不从源码补建缺失内容。`RuntimeManifestEnvelope` 对当前格式执行严格 magic 和内容校验；不存在旧格式 fallback 或 schema migration。

`FileRenderTargetArtifactProvider` 只读取部署内容，返回 `Ready` 或 `Unavailable`，不会伪造 Editor 的异步 `Pending` 状态。损坏的 Shader envelope 或空 Texture artifact 会抛出严格数据异常；Player 不调用编译器进行运行时补救。

## 生命周期与错误

- `RuntimeSession.Tick` 先刷新 Event、Coroutine 和 Job，再按 fixed accumulator 推进 Scene；Edit Session 不执行游戏生命周期。
- `RuntimeSession.Dispose` 释放 Scene、Asset、Scheduler、Serialization generation 和 Session Log；`EngineHost.Dispose` 会逆序释放仍存活的 Session。
- Host 或 Session 的 Dispose 会聚合所有阶段失败，不因第一个异常跳过后续清理。
- Session 日志携带明确 `LogSessionId`，Editor Console 不再根据 Assembly Scope 猜测来源。

[下一页：Player](Inno.Player.md)
