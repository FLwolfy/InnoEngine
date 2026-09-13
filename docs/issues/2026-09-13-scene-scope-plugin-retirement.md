# Scene View System 失效与 Play 后插件退休失败

[Issues 索引](README.md) · [Rendering Runtime](../render/Inno.Rendering.Runtime.md) · [Scripting](../editor/Inno.Editor.Scripting.md)

## 结论

本轮修复两个独立缓存生命周期错误，并补齐显式重载请求失焦时仍应推进的调度行为。没有修改卸载超时、跳过 GC、吞掉退休异常或引入 2D 专属引擎分支。

### Scene View 不更新

`Rendering2DSceneScopeCache` 原来只比较内容根身份。System 增删不改变 Scene identity，而 Scope 在构造时已经筛选了参与 2D 的 Scene，所以首次加入渲染 System、移除最后一个 System 后仍复用过期参与集合。

现在同时比较每个内容根的 `GetSystems()` 不可变快照。检查覆盖尚未参与 2D 的场景；成员变化时重建 Scope，稳定帧复用快照。Undo/Redo 和替换 System 使用同一失效路径，无需手工刷新视口。

### Reload Plugins 停在 97%

普通 Edit 模式的几种增删流程未触发错误。加入 Play → Stop → Reload Plugins 后，隔离的 TestProject 副本稳定重现原日志：30 秒、120 次 full GC 后，四个 module monitor 仍报告退休未完成。

托管堆检查进一步区分 monitor 列表与实际存活 ALC：此时只剩一个旧插件 ALC 和三个当前 ALC。旧插件的关键引用链是：

```text
RenderRuntime 的 Pipeline generation 缓存
  → 已退出 Play 世界的 RenderPipelineAsset
  → ConditionalWeakTable dependent value
  → 旧代 Rendering2D PipelineSettingsCache / Settings
  → 旧插件 LoaderAllocator
```

修复分两层：

- 引擎：缓存捕获资产注册身份，在帧边界及 reload 候选捕获前检查原 owner；身份已退休/替换时先通过现有退休协议释放 Pipeline/Feature，再移除资产缓存。宿主未注册的自建 Pipeline 保持 Runtime 所有权语义。
- 插件：Pipeline 设置缓存不再使用“宿主资产为 key、collectible 插件对象为 dependent value”的静态 CWT。改为插件私有、按 Identity 解析 owner 的缓存，清除已失效 owner；不创建第二套对象身份系统。

另修复菜单/API 显式重载在 Editor 失焦时可能一直排队的问题；自动文件变化请求仍遵循原来的焦点策略。这不是 97% 引用泄漏的根因，两者分别验证。

## 验证

环境：macOS arm64、Apple M2（8 GPU cores）、Metal；`/Users/aaronliao/.dotnet/dotnet`。

| 验证 | 结果 |
| --- | --- |
| Editor Application build | 0 warning、0 error |
| `dotnet test tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj --no-restore --disable-build-servers -m:1 -v quiet` | 116 通过 |
| `dotnet test tests/rendering/Inno.Rendering.Runtime.Tests/Inno.Rendering.Runtime.Tests.csproj --no-restore --disable-build-servers -m:1 -v quiet` | 57 通过 |
| `dotnet test tests/editor/Inno.Editor.PlayMode.Tests/Inno.Editor.PlayMode.Tests.csproj --no-restore --disable-build-servers -m:1 -v quiet` | 25 通过 |
| 更新引擎 + 原已安装 `.iplugin`，Play → Stop → Reload | 30,000 帧、正常退出；无退休异常 |
| 两仓库最新源码，连续三轮 Play → Stop → Reload | 30,000 帧、正常退出；无退休异常 |
| 第三轮后托管堆 | 仅当前 Runtime/Editor 两个 ALC（ID 7/8）；协调器 Ready，barrier/failure 为空 |
| Scene Scope 公开 API 探针 | 首次加入、移除最后一个、替换、Undo、Redo 全通过 |
| Scope 稳态缓存 | 预热后 1,000 次读取复用同一个 Scope，当前线程 0 字节分配 |
| 两仓库 `git diff --check` | 通过 |

测试脚本放在隔离临时项目，未修改仓库 tests。未修改原 TestProject 内容或安装包，未提交 commit。Windows GPU 与整个渲染性能矩阵不属于本轮验证范围；上述 Scope 分配结果不代表整个 Editor 零分配。

本机证据：`/private/tmp/inno-reload-play.log`（修复前失败）、`/private/tmp/inno-reload-play-loader-root.txt`（引用链）、`/private/tmp/inno-reload-play-fixed.log`（已安装插件回归）、`/private/tmp/inno-reload-combined.log`、`/private/tmp/inno-reload-combined-state.txt`、`/private/tmp/inno-scope-regression.log`。堆转储只保存在本机临时目录，未上传。

## 使用更新

必须重启到新构建的 Editor；已经进入 Faulted 的进程不能原地恢复。Rendering2D 开发工程直接使用修改后的源码；其他项目安装的 `.iplugin` 不会自动跟随开发仓库变化，需通过正常 Export as Plugin 流程重新导出并替换安装包，才能包含 Scene View 的 System 失效与插件缓存修复。

本轮保留了原有 `extern/cimguizmo` 状态以及 `Tools/Validate-Rendering2D.sh` 的既有修改。
