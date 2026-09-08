# Inno.Engine.Default

[Runtime 索引](README.md) · [Wiki 首页](../README.md) · [Shell](Inno.Shell.md) · [Adapter catalog](Inno.Adapter.Default.md)

## 定位

这是 Composition 中的默认引擎装配政策，不是 backend，也不是全局 Service Locator。它引用中立 adapter catalog 和领域 Runtime；具体 SDL3/BGFX/MiniAudio 选择仍由 `Inno.Adapter.Default` 的实现工厂完成。

```text
Shell
  ├─ EditorHost / GamePlayerHost
  ├─ Inno.Engine.Default → generated subsystem factory catalog
  └─ Inno.Adapter.Default → backend-neutral factory → concrete adapter → native
```

## 公开入口

| API | 语义 |
| --- | --- |
| `EngineSessionComposition` | 显式绑定 session、adapters、selection、input、artifacts、audioSettings 和可选 audioOverride；不查找隐藏全局状态 |
| `DefaultEngine.CreateSessionSubsystems(context)` | 生成 Input、Storage、Animation、Audio 的 factory 集合；Scene 模拟由 RuntimeSession 自身的 Scene bridge 加入 |
| `DefaultEngine.CreateHostSubsystems(renderRuntime)` | 生成 Host 范围的 Rendering factory；Editor 与 Play 不各自提交一次同一 GPU device frame |

内部 `Audio/`、`Input/`、`Storage/`、`Animation/`、`Rendering/` 文件各声明本领域装配。Audio 初始化失败明确报告 muted，Animation 创建 TypeCatalog-driven binding runtime；Reporter 和辅助 registration 转交 context.resources，最终由 pipeline 逆序释放。

Session 级 Reporter 的 source ID 必须包含实际 owner 范围。Animation 使用 `RuntimeSubsystemContext.identities.domainId`，因此共用一个 DiagnosticHub 的 Edit、Play 和其他并存 Session 不会互相撤销 Reporter。这个 domain ID 只区分运行期诊断所有权，不写入资产或 History。相同 source 的新 Reporter 仍会撤销旧代，过期发布仍明确失败；不能通过吞掉异常或取消代际检查来处理会话冲突。资源顺序保持为先创建 Reporter、再创建借用它的 binding owner，退出时先释放 bindings、最后释放 Reporter。

生成器通过 `OutputItemType="Analyzer"`、`ReferenceOutputAssembly="false"` 引入，不是运行时依赖。Editor 可传入自己的 session audio factory，以保留预览/Play 管理；它不需要复制整个默认子系统列表。

## 错误与扩展

重复 ID、错误 owner lifetime、缺少依赖或循环依赖拒绝启动。设备选择是中立 enum，不允许在 Host public/protected API 返回 concrete adapter 或 Native handle。这里不自动安装、创建或启用任何玩法 Plugin。

### Edit / Play 诊断隔离验证（2026-09-08）

原先 Animation 使用固定 source `inno.animation.bindings`，创建 Play 会话会撤销仍在使用的 Edit Reporter；随后更新或退出触发 `DiagnosticReporter` 过期异常，并使退休 gate 进入 Faulted。修复位于默认工厂的 source 构造，不改变 Core Reporter 的代际保护或 GC 屏障。

通过公开 `DefaultEngine.CreateSessionSubsystems` 和 `EngineHost.CreateSession` 的仓库外验证程序，修复前复现相同退休异常；修复后通过 32 次 Edit/Play 循环、三个并存会话、不同退出顺序、Missing 诊断互不覆盖与 Host 正常释放。现有 Diagnostics、Animation、Runtime、Editor PlayMode、Editor Audio、Editor Scripting 六个测试项目共 128 项通过；Solution 零警告/错误，Architecture 通过。本次未修改 tests，不把这些程序化验证称为真实 GUI 点击验收或全量发行验收。
