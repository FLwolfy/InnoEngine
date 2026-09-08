# Inno.Runtime.Contracts

## Pending 与终态清理错误

`RuntimeSubsystem.Dispose` 先取消 tracked work；未静止时抛出 Foundation 的 `RetirementPendingException`，不执行 OnStop。Pipeline/Session/Host 必须保留该 owner 与依赖，完成后重试。不能将 pending 包装成普通 AggregateException 后继续销毁依赖。真正执行 OnStop 后发生的清理错误才聚合报告。

这一协议不承诺能强制终止任意插件线程，也不允许在后台线程释放控制线程资源。

[Runtime 索引](README.md) · [Wiki 首页](../README.md) · [Runtime owner](Inno.Runtime.md) · [默认装配](Inno.Engine.Default.md)

## 职责与依赖

这是引擎子系统的中立生命周期协议，只依赖 Foundation 的 Events、Diagnostics、Execution、Identity 与 TypeCatalog。它不引用 Scene、Assets、Rendering、Audio、Editor 或 backend。领域 Runtime 引用本项目，不引用包含 Scene/部署逻辑的 `Inno.Runtime`。

## 公开 API

| 契约 | 稳定语义 |
| --- | --- |
| `IRuntimeSubsystem` | Attach、BeginFrame、FixedUpdate、Update、LateUpdate、BeforeRender、Render、AfterRender、EndFrame、Detach、Dispose 的有限生命周期 |
| `RuntimeSubsystem` | 上述非虚公开入口的模板实现；统一 owner thread、frame scope、异常退出、取消与逆序释放 |
| `IRuntimeSubsystemFactory.descriptor` / `Create(context)` | 声明稳定身份、依赖和 owner 范围，构造独立实例 |
| `RuntimeSubsystemId` | 开放 semantic ID；不是 Object Identity 或 CLR 类型名 |
| `RuntimeSubsystemDescriptor` | 冻结 id、order、dependencies、lifetime、requirement、requiredCapabilities；拒绝非法 enum、空 ID、自依赖和重复声明 |
| `RuntimeSubsystemRequirement.Required/Optional` | Required 失败阻止 owner 启动；Optional 只有完成补偿后才能进入 Unavailable，不能吞清理失败 |
| `RuntimeCapabilityId` | Composition 验证后提供的开放、区分大小写能力 ID，不是 backend enum 或 live object Identity |
| `RuntimeSubsystemLifetime` | Host 与 Session 是不同资源所有权，不能把 Host GPU runtime 塞进 Session pipeline |
| `RuntimeFrame` / `RuntimeFixedFrame` | 中立时钟值；不携带 Session、Scene 或设备 |
| `RuntimeSubsystemContext` | 显式 events、diagnostics、identities、types、resources、lifetime、persistentDataDirectory、isEditMode、冻结 capabilities；不是任意服务查询容器 |
| `RuntimeSubsystemRegistrationAttribute(id)` | 在发行程序集声明一个强类型 factory 方法 |
| `RuntimeSubsystemCatalogAttribute` | 要求构建期生成同一 composition 参数类型的 catalog |

## 派生扩展点

`OnStart` / `OnStop` 处理领域资源；`OnBeginFrame`、`OnFixedUpdate`、`OnUpdate`、`OnLateUpdate`、`OnPrepareOutput`、`OnProduceOutput`、`OnCompleteOutput`、`OnEndFrame` 处理有限帧阶段。`OwnFrameScope` 登记必须退出的 façade binding；`lifetime` 跟踪该子系统自己的资源、取消与工作。

```csharp
using Inno.Runtime.Contracts;

public sealed class CounterRuntime : RuntimeSubsystem
{
    public double elapsed { get; private set; }

    protected override void OnUpdate(RuntimeFrame frame)
    {
        elapsed += frame.unscaledDeltaTime;
    }
}
```

示例省略 XML 注释；实际公开实现必须补齐。`AudioRuntime : RuntimeSubsystem, IAudioService` 组合 `IAudioDevice`；不能为了句柄编码继承 AudioDevice。Layer/LayerStack 留在 Core，负责局部 UI/应用层顺序；Pipeline/Mixer Feature 只贡献领域行为。

## 失败与生命周期

重复 Attach、错误线程、无 BeginFrame 的阶段调用会失败。帧入口失败会关闭已进入的作用域；Dispose 尝试逆序资源清理并聚合错误。尚未完成的 tracked work 不允许被当作已释放，必须先取消并排空；模板不是线程强杀器。

完整 DAG 排序、缺失依赖/环检测与逆序阶段展开由 `Inno.Runtime` 的 `RuntimeSubsystemPipeline` 实现。GenerationCoordinator 管 collectible 扩展代际，Subsystem 不建立第二套 GC barrier。

Pipeline/Session **先被 Host 持有，再运行 factory/Attach**。每个 factory 的 `context.resources` 是独立 lifetime，
可选系统失败不会释放其他系统的资源。Factory 必须将尚未交给返回实例的资源与 Task 登记到该 lifetime；
引擎无法自动接管用户在 factory 内部偷偷保存到静态字段的资源。

`Attach` 失败不自行进行一次不完整的 Dispose；外层 owner 负责完整排空。`OnStop` 抛 Pending 时允许重试，
其完成后即使后续 lifetime Pending，也不会再次执行已完成的 `OnStop`。

## 能力与失败政策

```csharp
using Inno.Runtime.Contracts;

var descriptor = new RuntimeSubsystemDescriptor(
    new RuntimeSubsystemId("sample.capture"),
    lifetime: RuntimeSubsystemLifetime.Host,
    requirement: RuntimeSubsystemRequirement.Optional,
    requiredCapabilities: [new RuntimeCapabilityId("sample.frame-capture")]);
```

能力集合由已经检查设备/服务的 Composition 传入，Runtime 不猜测当前 OS 或 backend。
缺失能力/依赖不会调用该 factory。Optional 依赖不可用时，它的 Optional consumer 同样不可用，
Required consumer 则拒绝 owner。重复 ID、cycle、生命周期错误始终是配置错误，Optional 不豁免。
运行帧异常和退休异常不属于 Optional 的降级范围；当前政策明确传播失败，不提供自动重启/静默跳帧。
