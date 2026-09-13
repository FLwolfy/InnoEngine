# Shader 创作体系阶段验收报告

[实施计划](2026-09-12-shader-authoring-completion-plan.md) · [实施记录](2026-09-12-shader-authoring-execution.md) · [Issues](README.md)

## 结论

**主要实现已落地，但 P1–P6 尚未达到整体验收完成条件。** 不以单元测试或启动成功代替完整 UI、像素和性能验收。未自动提交 commit，Windows GPU 按约定暂缓。

## 阶段与变更边界

| 阶段 | 已落地内容 | 状态 |
| --- | --- | --- |
| P1 | 稳定 Target/模板注册、generation 快照、独立 `Inno.Editor.Shaders`、逻辑脚本 API；Rendering2D 实际贡献 Target、模板、Inspector、GPU 预览 | 实现及相关测试通过；完整扩展/卸载组合验收未穷尽 |
| P2 | 共享阶段计算、独立 Pass 状态、删除/复制/粘贴关联声明、每手势独立 History；五种混合模式复用计算 | 结构/编译测试通过；覆盖和遮挡的像素回归未完成 |
| P3 | Material Inspector、类型化参数与输入、override/reset、多选、参数说明/范围；Shader/Material 显式 Save/Revert、恢复、冲突检测、隔离 GPU 预览 | 自动化工作流通过；全部真实鼠标交互和截图验收未完成 |
| P4 | 单一 Pipeline 配置权威、owner-aware 嵌套引用与导出依赖；失败创作输入拒绝构建；异步导出持有 generation 租约 | 真实 Player 导出/运行及相关失败测试通过；完整安装/移除/恢复矩阵未穷尽 |
| P5 | 四节点 DefaultSprite、高层 Sprite Target、可选顶点偏移、独立 Mask 材质、保守 bounds、六个内部 Shader | Metal 灯光/阴影/Bloom/resize 通过；大规模 GPU 与像素等价验收未完成 |
| P6 | 执行全量测试、实际脚本 IDE 构建、Metal 压力与 Player；隔离 legacy 缓存、更新文档 | **未通过整体验收**，见剩余项 |

### 引擎本体

- Shader Target/模板将高层图展开为现有公共图/IR，不认识 Sprite、Light2D、Camera2D 或 Bloom。
- 可复用 Editor 功能层拥有 Shader/Material 创作协议、预览和 Drawer；具体 Panel 不再拥有公开节点绘制协议。
- 通用资产源存储提供 detached draft、原子 Save、外部冲突检测；owner-aware 属性快照自动捕获嵌套资源依赖。
- Runtime 资源服务支持隔离草稿产物的真实 GPU 预览与帧边界退休，不污染正式资产发布键。
- 脚本 API reference 投影保留本次实际消费者需要的 nullable/泛型使用与条件非空信息；实际 IDE 严格构建验证通过，不用 `null!` 掩盖合法可空参数。
- Player host 将既有 Settings owner scope 覆盖整个运行循环，包括渲染提取；不是新增 2D 特例。
- Player 内容导出沿 Artifact/Source 输入校验失败、缺失和过期依赖。Editor last-good 仍可使用，但不能认证当前错误源码的构建。

### Rendering2D

- `Sprite Texture → Multiply ← Tint Parameter → Sprite Surface Output` 为默认创作图；Sprite Target 提供默认顶点、资源接口及五种混合 Role。
- LightAccumulation、ShadowStencil、BloomPrefilter、BloomDownsample、BloomUpsample、FinalComposite 由 Pipeline 显式引用。不是每盏灯或每级 Bloom 一份资产。
- 内部 Shader 全部沿图、函数源码、公共 IR、Adapter 链编译；不再通过负数 `v_shape` 操作码或 Sprite 实例字段传递后处理参数。
- 默认材质只服务需要它的对象；有效自定义材质不因默认材质缺失而整帧消失。必需 Mask 失败明确使输出不可用，不静默绘制无 Mask 结果。
- 顶点偏移进入同一编译链；Mask 可以使用相同覆盖材质。`boundsPadding` 由作者提供保守包围范围，不能自动推导任意程序变形。ShadowCaster 的多边形仍是独立几何，不宣称能自动求解任意 Shader 的阴影轮廓。

这些机制可由未来 3D 或自定义管线复用；新 GPU 阶段、原生指令或设备能力仍可能要求底层契约和 Adapter 支持。

## 已执行命令与证据

以下路径为本机此次运行记录；命令中的引擎根目录是 `InnoEngine`，dotnet 使用 `/Users/aaronliao/.dotnet/dotnet`。

| 验证 | 结果 | 本机日志 |
| --- | --- | --- |
| 最后 `dotnet build InnoEngine.sln --no-restore --disable-build-servers -m:1 -v quiet` | 0 warning / 0 error，包含最新导出生命周期修改 | `/private/tmp/inno-authoring-final-solution-build.log` |
| `dotnet test InnoEngine.sln --no-restore --disable-build-servers -m:1 -v quiet` | 49 项目；1248 passed / 1 failed / 0 skipped | `/private/tmp/inno-authoring-final-solution-tests.log` |
| 最后新增的异步导出成功/失败/取消用例后，单独复测 Assets.Pipeline | 106 passed，0 failed；不与上行重复累计为另一轮全量结果 | `/private/tmp/inno-authoring-export-lifetime-final.log` |
| 完整 Scripting suite | 116 passed，包含实际 ShaderPreview 的可空参数及非空泛型回调 | `/private/tmp/inno-authoring-nullability-contract-tests.log` |
| 临时真实 `Inno.EditorScripts.csproj`：`build --disable-build-servers -m:1 -warnaserror -p:NuGetAudit=false` | 0 warning / 0 error | `/private/tmp/inno-authoring-ide-contract-final.log` |
| Editor：`--graphics-api metal --smoke-frames 12000` | 正常退出，无活动 Error；此项不等于 UI 人工操作验收 | `/private/tmp/inno-authoring-final-editor-smoke.log` |
| `DOTNET_COMMAND=/Users/aaronliao/.dotnet/dotnet bash Tools/Validate-Rendering2D.sh <engine-root>` | **完整脚本 exit 0**；60,000 帧、效果/resize/资源/CPU 门槛和末尾 IDE 严格构建通过 | `/private/tmp/inno-authoring-final-metal-script.log` |
| BuildCLI 生成的真实 Player：`--graphics-api metal --smoke-frames 1800` | exit 0；views=15，draws=92，dispatches=0 | `/private/tmp/inno-authoring-player-current-metal.log` |
| 临时必需 Bloom source 注入 `#error` 后再次 BuildCLI 导出 | 正确失败；报告 `.ishader → .ishadersource:2`，未用 last-good 冒充成功；注入已撤回 | `/private/tmp/inno-authoring-invalid-export-verified.log` |
| 顶点偏移实际 Metal 编译 | 默认/连接计算各生成五个 Pass；Fragment-only 输入连接顶点偏移明确拒绝 | `/private/tmp/inno-sprite-vertex-acceptance.log` |

最后的异步导出修复不改变 Metal 程序；资产测试之后已用最新 BuildCLI 重新完成实际 Player 导出（`/private/tmp/inno-authoring-player-verified-build.log`，`VerifiedOutput`），不是手工替换 DLL；该产物再次完成 Metal 1800 帧、正常退出（`/private/tmp/inno-authoring-player-verified-metal.log`）。整条 60000 帧 Editor GPU 脚本未因这一资产生命周期修改重复计数。

## Metal 与性能数据

本机：MacBook Air，Apple M2（8 GPU cores），16 GB，macOS Metal。未单独采集可复现的 GPU 驱动版本和全帧 GPU 时间，不能据此宣称跨驱动等价。

完整 Metal 脚本验证了：35 灯、38 个 light draw、4 个 shadow-volume draw、HDR/MRT/D24S8、五级 Bloom；96 次 Game View 目标尺寸变更均完成真实 framebuffer 重建；随后稳定 120 帧进入资源门槛，之后没有再次创建 render-target texture。

同一次运行的 240 次稳定 CPU 样本：

| 测量范围 | P95 | 托管分配 |
| --- | --- | --- |
| Scene 提取 | 0.112 ms | 0 bytes |
| Runtime camera 提取 | 0.117 ms | 0 bytes |
| Camera stack 提取 | 0.119 ms | 0 bytes |
| Request 提取/提交 | 0.117 ms | 0 bytes |
| 百万格稀疏域中十万可见 Tile、32 灯的提取 | 0.097 ms | 0 bytes |

**这些是缓存命中的 CPU 提取/请求测量，不是十万实例真实 GPU、总帧时间或完整 Runtime 零分配证明。** 测试时存在编译/测试并行负载，未建立相同条件的前后 GPU 基线；P50、显存、全部 draw/batch/instance、全帧 P95 16.6 ms 和 10% 回归门槛仍未完成验收。

## Legacy 与数据保护

- 当前 `src`/`build` 的 `.cs/.csproj/.props/.targets` 未检出旧 MaterialGraph/`.imaterialgraph` 执行链；当前 Shader/Editor 通用实现未检出 Sprite/Light2D/Bloom 中央分支。
- 当前 Player 包未携带 Editor、`Inno.Rendering.Shaders`、`Inno.Core.Graphs`、源码解析器或 `.ishader/.ishadersource` 创作文件。
- 两个废弃 MaterialGraph 项目的 bin/obj 目录与 13 个旧 ScriptApi generation 缓存移动到 `/private/tmp/inno-materialgraph-cache-quarantine.LYILxV`，可恢复；未删除整个工作区缓存。
- Rendering2D 原作者图和函数保存在 `Documentation/ShaderAuthoringBaseline`，不在 Assets 导入或 Player 闭包内。不是兼容加载路径。
- 不覆盖无关用户修改；当前 Git 工作树仍包含此前的未提交变化，不能把整个 diff 都归为本轮。

## 必须继续完成的验收

1. **真实 UI 阻塞**：最后一次窗口检查返回 `The Mac is locked and automatic unlock could not unlock it`。需要用户解锁 Mac，才能继续鼠标/快捷键、Material 各输入、多选、保存/关闭、浮动/停靠、Tooltip/DPI 以及截图验收。此前原生绘制/手势测试和局部可视检查保留，但不能替代全部窗口闭环。
2. **全量测试仍红**：`OverlayScrollbarsDoNotReserveWindowContentWidth` 期望 0、实际 14。9 月 11 日的旧报告已记录同一失败；本次未修改相关 native overlay 实现或放宽测试。当前不能报告全 Solution 全绿。
3. 十万实例真实 GPU、动态文本以外本次范围的粒子/灯光组合、全帧 GC/CPU/GPU/显存、P50/P95 和 10% 前后回归证据仍未完备。
4. Mask/顶点覆盖、不同混合模式和光照/后处理的固定截图像素容差回归，以及完整安装/移除/恢复组合还需执行。

Windows GPU 验证是用户明确暂缓，不冒充已通过；Inno.Text 和 3D/PBR 不在本次范围。以上当前范围验收未通过前，不称“P1–P6 全部完成”。
