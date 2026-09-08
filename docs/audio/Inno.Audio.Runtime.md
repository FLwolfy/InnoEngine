# Inno.Audio.Runtime

## Provider 所有权与恢复诊断

Provider 实例在 `AudioExtensionRegistry` 的 TypeCatalog 候选快照构造阶段创建，由该快照独占释放；Runtime 不再维护第二套延迟创建/释放的 Provider generation。所有 Provider 都尝试退休，清理失败经 TypeRegistry 将共享 generation gate 置为 Faulted。它不是可回滚的普通候选失败。

Mixer 恢复成功会移除先前该配置的 Missing/候选失败诊断。旧 Bus 仅等待实际路由到它的 Voice，而不是等待整个引擎静音；安装和 Update 安全点均可回收已无 Voice 的代际。

`AudioRuntimeOptions.maxPendingPreloads` 默认 256，限制尚未完成的 preload 等待者。超限明确拒绝，停止后不能通过 Bus 控制入口访问已释放设备。

`maxContentSnapshots` 默认 1024，限制每次 Update 中所有 Provider 成功贡献的 emitter + listener 总量，
不等同于播放 Voice 预算。Runtime 按 priority、stable provider ID 顺序逐个分配剩余容量；Provider 单独收集，
成功返回且全部 identity 验证通过后才整批接纳。失败、超限、内部吞掉提交异常或跨 Provider 重复，都丢弃该 Provider
的全部贡献，发布 `AUDIO_CONTENT_PROVIDER_FAILED`，不占用下一 Provider 的容量。下次成功后诊断自动解除。
该 Provider 在本帧没有贡献的旧 emitter 按常规 absent-content 规则停止，而非继续播放部分失败数据。

每个 provider context 在调用结束后撤销并清除 Clip 引用，整个 host content scope 在 Update 返回时关闭。
`AudioRuntime.contentStatistics` 返回最近一次 collection 的 `AudioContentStatistics`：`capacity`、
`acceptedSnapshots`、`rejectedProviders`，只包含中立计数，无 Provider/Scene/Asset 引用；它是 Host 诊断入口，
不扩展设备 ABI 或游戏 `Audio.statistics`。设备 generation 替换后计数重新开始。
`RetirementPendingException` 穿过内容隔离边界，不伪装成一般 Provider 失败。

内部资源由明确的 owner 独占，不属于 public API，也不是 partial 文件拆分：

| Owner | 独占状态与责任 |
| --- | --- |
| `AudioClipCache` | 原生 Clip、Artifact lease、preload waiter、decoded budget。 |
| `AudioVoiceOwner` | Runtime/backend handle 映射、调度、抢占、终止状态、Clip 引用与完成事件。 |
| `AudioMixerOwner` | Bus、控制状态、候选 graph、被 Voice 保留的旧 graph。 |
| `AudioContentOwner` | Provider 的单次内容消费、emitter 增量记录、listener 选择与诊断。 |
| `AudioDeviceCandidate` | 未发布设备及其 Mixer 的准备、所有权转交与失败退休。 |
| `AudioRuntime` | `IAudioService` 门面、owner 组合、帧顺序、设备恢复与生命周期。 |

普通退休失败会继续尝试其余资源并聚合；`RetirementPendingException` 不属于普通失败：保留当前 owner、Clip Artifact lease、未处理 Bus 和下层 device，重试从当前阶段继续，禁止继续播放。Provider 也必须重试真实 Dispose hook，不能提前置 disposed。设备替换使用 Core retirement deadline；超时由 TypeRegistry 封锁共享 generation gate，不能包装成可恢复的 `false`。

完成事件入队遇到背压时保留终止中的 Voice，下一次安全点重试；已释放的 Clip 引用不会重复递减，事件只成功发布一次。终止操作仍可能报告明确背压错误，Host 需要排空当前 EventDispatcher 后继续。

新请求先校验显式 options 并取得 Artifact 请求，再执行预算抢占；未初始化 options 或 Artifact acquisition 的直接
Pending 不改变旧 Voice。普通 Missing/metadata/解码失败仍遵循 Preparing → DecodeFailed 完成事件，不改成同步
异常，也不承诺所有异步失败都不会消耗 Voice 预算。抢占遇到事件背压时，新请求的 Artifact 交给共享 generation
retirement barrier 释放；重试旧 Voice 的完成步骤，不重复 Stop 或增加 stolen 统计。

每个缓存项内部拥有 Core `LifetimeScope`：先释放 native Clip，再释放 Artifact，Pending 时保持当前步骤。
已开始退休的条目不再被新 Voice 复用；重试不会再次销毁已成功退出的 Clip。ReleasePreload 的计数只扣一次，
待退出条目由后续 Update 安全点继续处理。Update 中取消/失败的异步 preload 在资源退出前保留 waiter，不提前报告完成或
把 Pending 变成解码错误。命中已有缓存时，新请求多取得的 Artifact 也经共享 retirement barrier 清理。

首次 `GetClipState` 成功且未返回 Failed 后才增加 preload 引用。查询异常或准备失败会退休本次新建且无人引用的
cache entry；已有 Voice/preload 的缓存项保持原引用。回滚中 Pending 使用相同 barrier 排空，普通回滚失败与
原查询异常一起保留并 Fault 共享 generation，不能以“准备失败”掩盖资源清理失败。

`ReplaceDevice` 消费已验证的 replacement：候选图构建失败会释放候选，保留旧设备；开始退休旧设备后发生的失败不可伪回滚，会尝试全部资源清理、释放候选并 Fault 共享 generation。`TryRecoverDevice` 不会把这一类失败转换为 `false`。成功恢复会撤销 no-device、device-lost 和 recovery-failed 状态诊断。

## 本轮统一收口

`AudioRuntime : RuntimeSubsystem, IAudioService` 组合 IAudioDevice，不继承设备。公共 Voice 使用 `AudioVoiceHandle` 与 runtime owner generation；backend Voice 使用独立 `AudioDeviceVoiceHandle` 与 device generation，不能互换。

`PreloadAsync` 现在启动真实 native 准备，后端通过 `GetClipState` 返回 Preparing/Ready/Failed；等待者只在准备成功或失败时完成。Preparing 期间通过 Update 安全点处理取消与完成，关闭/设备替换会取消未完成等待。不得在停止 tick 的 owner thread 上同步等待 preload。

Play 立即返回 Preparing 句柄。请求捕获 metadata、内容 revision 和 `audio-data` 的 ArtifactLease，不跨帧持有 AudioClipAsset。Clip cache 按 persistent ID、content revision、artifact key 和 load mode 区分；租约在最后一个 voice/preload 释放后退休。Emitter record 同样只保存 ID 与 revision，避免比较同一个被原位更新的 Asset 引用而漏掉变化。

所有问题进入注入的 `IDiagnosticReporter`，内容提供者读取统一 `ContentReadScope`。Session 的 reporter/registration 由 pipeline construction lifetime 持有。Runtime options 在构造时复制，修改调用者的 options 不会偷改活动预算。

[Audio 索引](README.md) · [Core contract](Inno.Audio.md) · [MiniAudio](Inno.Adapter.Audio.MiniAudio.md)

`Inno.Audio.Runtime` 实现控制线程上的播放编排，不认识 Scene、Editor 或具体后端。`AudioRuntime` 同时实现 `IAudioService`，并拥有一个 `IAudioDevice` generation。

## 公开 API

| 类型 | 作用 |
| --- | --- |
| `AudioRuntime` | 播放/预加载、Mixer 安装、设备替换/恢复、Provider 同步、每帧安全点更新与释放。 |
| `AudioRuntimeOptions` | `maxVoices`、`maxPendingPreloads`、`maxContentSnapshots`、decoded cache budget、automatic stream threshold 与 device recovery interval。 |
| `AudioContentStatistics` | 最近一次内容收集的容量、接纳 snapshot 数与拒绝 Provider 数；通过 `AudioRuntime.contentStatistics` 读取。 |
| `AudioProjectSettings`（Inno.Audio） | 可部署的 default mixer、master volume 与 Runtime budgets。 |
| `MutedAudioDevice` | 无声卡或测试环境下明确的静音设备；时钟、状态和完成仍推进。 |
| `ContentReadScope`（Inno.References） | Host 提供的一帧 Identity-backed 内容边界，使用后必须释放。 |
| `AudioEmitterSnapshot`、`AudioListenerSnapshot` | 不含 Scene 类型的 emitter/listener 状态。 |
| `AudioContentProviderExtensionAttribute`、`AudioContentProvider`、`AudioContentProviderContext` | TypeCatalog 自动发现的控制线程扩展协议。 |

## 更新与 generation

TypeCatalog 在统一候选事务中准备和退休 Provider；`Update(deltaTime)` 在控制线程消费已发布 generation、同步 emitter/listener、推进 backend、收集完成并派发事件。候选构造失败且清理成功时保留 last-good；清理失败必须 Faulted。不得在音频 callback 中刷新 Catalog。

Voice stealing 在 `maxVoices` 达到上限时按 priority、创建序列和稳定 handle 确定性选择。Clip cache 按 Asset persistent ID、content generation 和 load mode 分键；预加载引用与 live voice 引用独立，旧不可变 Artifact 会保留到使用它的 Voice 结束。Automatic 会在 encoded threshold 或估算 decoded footprint 超过预算时选择 Stream；显式 Decode 超过预算则明确失败。

```csharp
using var audio = new AudioRuntime(
    host.types,
    device,
    session.assets,
    session.events,
    diagnostics,
    new AudioRuntimeOptions { maxVoices = 64 },
    contentScopeProvider);

// Standalone control-thread use only; a Session pipeline already updates its owned AudioRuntime.
using (audio.EnterExecutionScope())
    audio.Update(deltaTime);
```

MiniAudio 原生线程不执行 provider、反射、Asset IO、托管日志、锁或托管 DSP callback。扩展只在控制线程提交中立 snapshot/graph，设备只消费已构建状态。

## 失败语义

- 设备初始化失败由组合根显式建立 `MutedAudioDevice` 并发布诊断，不伪装为正常输出。
- `ApplyMixer` 的候选无效时返回 `false` 并保留当前 graph。
- `ReplaceDevice` 使旧 generation 句柄失效，并在候选设备上先重建 last-good Mixer graph；设备丢失的 Voice 以明确 completion reason 收敛。
- Host 提供 recovery factory 时，Runtime 按 `deviceRecoveryIntervalSeconds` 在 `Update` 安全点重试；候选初始化或 graph 构建失败不会替换当前 generation，成功后保留 Bus volume/mute/pause 控制状态。
- Dispose 停止 Voice、释放 Clip/Bus/Listener、provider generation 和设备，重复释放安全。
- `Update` 在执行 Provider 前拒绝负数、NaN 与无限 deltaTime，不将无效时间静默改为零。

`MutedAudioDevice(AudioDeviceLimits? limits = null)` 使用与官方 native backend 一致的 Clip/Bus/Voice + completion 接纳限制。静音不会意味着无限积累；调用者仍必须通过 Update 和 TryDequeueCompletion 推进生命周期。
