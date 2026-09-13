# Shader Target 启动导入时序修复

[审查索引](README.md)

## 根因

Editor 在编译、激活项目 EditorScripts 之前创建 AssetPipeline 并扫描资产。内置 Shader importer
已经存在，但项目脚本贡献的 `inno.rendering.2d.sprite-surface` 尚未注册。原实现把这段正常的发现窗口
当作永久导入失败；每次访问仍将 Failed 视为 stale，重复导入并打印异常。

此外，启动过程中普通程序集目录刷新也会创建候选资产目录，不能把任意一次目录发布当作项目脚本已就绪。
无 last-good 的 Shader 曾被当作可反序列化资产加载，造成引用它的 Material 继续报“序列化损坏”。
这不是 DefaultSprite 图被破坏，也不能通过删除 Library 或重建默认资产修复时序。

## 变更与边界

- `ShaderTargetUnavailableException` 仅标识当前 generation 缺少指定 Target；Target 内部计算、校验错误不进入该路径。
- Shader importer 将它转换为通用 `AssetImportExtensionUnavailableException`。资产系统不认识 Sprite、2D 或 BGFX。
- Editor/Build 组合入口明确开启初始扩展发现窗口，成功激活后调用 `CompleteExtensionDiscovery()`；初始脚本失败不关闭该窗口。
- 复用原有 `AssetImportStatus.Pending` 和 DiagnosticHub，等待状态为 Info，不伪装 Imported，不刷 Error 堆栈。
- 普通启动目录刷新保留初始窗口；初始发现完成后的 reload 候选在激活阶段恢复严格检查，新引入失败仍回滚。
- 缺失扩展的重试以 generation、源文件、导入设置、已知依赖变化或显式 Import 为依据，不按帧/计时器盲目重试。
- 重试缓存只持有稳定字符串、文件 stamp 和 generation 数字，不持有异常、Type、插件实例或委托。
- 缺少产物的记录不再反序列化空状态；导入依赖传播原缺失扩展原因。正式产物存在时仍保留 last-good 行为。
- 源图、`.imeta`、persistent ID 和已有成功产物不因等待被清空。只读插件侧车保持不变。
- 导出校验包括从未成功导入的 runtime 资产；不能因没有 artifact key 就从构建输入中静默消失。

没有把 Target 移入通用内核，没有恢复旧 Shader 入口，没有禁用真正的编译/导入错误。
本次未修改 Rendering2D 业务脚本、Shader、Material、Pipeline 或用户工程内容。
Rendering2D 仓库只加强了验收脚本：整个冷启动日志中出现资产导入失败即判失败，而非只看最终画面。

## 验证

环境：macOS arm64，Apple M2（8 GPU 核心），Metal，BGFX vendor `106b` / device `03f0`。
引擎基线 `caeb437a`，Rendering2D 基线 `2c8caf0`；保留原有 `extern/cimguizmo` 非干净状态，未提交 commit。

使用 `/Users/aaronliao/.dotnet/dotnet`，测试命令统一为：

```text
dotnet test <项目路径> --no-restore --disable-build-servers -m:1 -v quiet
```

| 范围 / 项目路径（相对引擎仓库） | 结果 |
| --- | --- |
| tests/assets/Inno.Assets.Pipeline.Tests/Inno.Assets.Pipeline.Tests.csproj | 112 通过 |
| tests/rendering/Inno.Rendering.Shaders.Tests/Inno.Rendering.Shaders.Tests.csproj | 125 通过 |
| tests/rendering/Inno.Rendering.Assets.Tests/Inno.Rendering.Assets.Tests.csproj | 27 通过 |
| tests/scripting/Inno.Editor.Scripting.Tests/Inno.Editor.Scripting.Tests.csproj | 116 通过 |
| Editor Application、Build CLI 构建 | 0 warning / 0 error |
| 隔离工程生成的 EditorScripts，`-warnaserror` | 0 warning / 0 error |

新增回归覆盖：初始等待、完成后确实缺失、显式恢复、重复访问不重试、真实坏内容不延迟、初始普通目录刷新、
严格候选失败与回滚、依赖路径/序列化引用传播、无产物加载、ID/源内容保留、只读侧车、Pending→Info / Failed→Error、
从未成功导入的资产阻止导出。

真实 Metal 验收：

```text
DOTNET_COMMAND=/Users/aaronliao/.dotnet/dotnet bash Tools/Validate-Rendering2D.sh <InnoEngine-root>
```

- 新 Library 冷启动 60,000 帧：资产导入错误 0，正常退出。
- 35 灯、38 light draws、4 shadow-volume draws、HDR/MRT/D24S8、五级 Bloom 通过。
- Editor Game View 连续 96 次 render-target resize 通过。
- 十万可见 Tile / 百万格稀疏域 / 32 灯测试：稳态提取 0 托管分配，P95 0.096 ms；这不是整帧 GPU 时间。
- 保留 Library 的隔离工程重启 12,000 帧：资产导入错误 0，同样通过 GPU 验证并正常退出。
- 无 Library 工程故意加入 `#error`：12,000 帧后只报告 1 个预期 `CS1029`，Shader/Material 导入错误 0。
  Smoke 因这个真实错误按规则返回 exit 1；该负向测试不冒充正常启动通过。
- 删除临时故障脚本、不清理该工程 Library，再运行 12,000 帧：恢复 GPU 验收，资产导入错误 0，正常退出（exit 0）。

证据：Rendering2D `Logs/Rendering2D-metal-GpuAcceptance.log`；本次临时验证日志
`/private/tmp/inno-extension-{assets-tests,rendering-tests,scripting-tests,validation,warm}.log`。
故障与恢复证据分别为 `/private/tmp/inno-extension-failure.log` 和 `/private/tmp/inno-extension-recovery.log`。
首轮中间修复的冷启动仍暴露了关联错误，该日志保留在 `/private/tmp/inno-extension-first-pass.log`，不计为通过。

Windows GPU 本轮未执行，也不以本机 Metal 结果代替。无需删除用户缓存或覆盖原 Shader；使用新构建的 Editor 重启即可。
