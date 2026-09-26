# 渲染、Canvas 与插件样例验收记录（2026-09-26）

[架构治理](README.md) · [渲染运行时](../render/Inno.Rendering.Runtime.md) · [样例导入](../assets/Inno.Assets.Pipeline.md)

## 结论

计划中的引擎契约、世界 Canvas、2D 组合、帧级 UI 更新、字体、Gizmo、样例导入、只读插件场景和 Player 排除规则均已实现。三个仓库构建通过，Engine 50 个测试程序集共 1,297 项通过，Canvas 6 项通过，架构门禁为 0 违规。TestProject 单独安装 Canvas 与 Rendering2D 两个插件，Editor 与 Player 均显示并可交互。

**整项计划仍未达到“完整验收”。** 多模型 route 已实现并通过 RenderGraph 合成测试，但没有第二个真实渲染模型进行 Metal 图像对比；高 DPI 字形清晰度、旋转及父级 Transform 的完整视觉与点击矩阵也没有逐项留证。以下报告把实现、已验证事实与未验证范围分开记录。

## 架构与运行流程

```text
Editor GameView / Player：RenderOutputSession（内容、目标、视口、输入、可选 route）
    → Inno.Rendering.Runtime：发现并仲裁 IRenderModel
       ├─ 单模型：直接生成 RenderRequest
       └─ 多模型：route 为每层指定模型与内容源
          → 各模型独立可采样目标 → 校验格式 → 预乘 Alpha 顺序合成
    → Rendering2D：Camera2D View、精灵与外部内容统一排序、遮挡与命中
       → IViewContentCollector
          → Canvas：实例 Context、RML 布局、字体、世界平面 Drawable
    → 所有输出收集输入 → Canvas 每模拟帧更新一次 UI → RenderGraph → GPU

SceneView：同一输出 Session + 2D 编辑导航/拾取 + Editor Gizmo Provider
Shader/Material Preview：独立预览请求，不参与场景模型合成
```

Canvas 不引用 Rendering2D；两个插件分别只依赖引擎中立契约。Canvas 仓库的 `Plugins/` 没有 2D 包；TestProject 的 `Plugins/` 中分别有 `InnoEngine.Canvas.iplugin` 和 `rendering2d.iplugin`。Canvas 插件样例场景只有 Canvas；TestProject 自有 `CanvasRuntimeDemo.iscene` 组合 Camera2D 与 Canvas。单独打开插件原始样例时，Game/Scene 显示“没有可用渲染模型”，不会由 Canvas 偷偷创建全屏输出。

`referenceWidth/referenceHeight` 固定 RML 的百分比布局，默认 800 × 450；项目 `logicalPixelsPerWorldUnit` 默认 100。世界平面为 8 × 4.5 世界单位，再乘完整父子 Transform Scale；位置和旋转来自 Transform。字体由 RML `@font-face`、`font-family`、`font-weight` 与 `font-size` 控制。材质由 Canvas 内部管理。2D 模型将 Canvas 与精灵排序在同一场景颜色阶段，再进入后处理。

## 功能验收

| 项目 | 状态 | 证据与边界 |
| --- | --- | --- |
| Canvas/Rendering2D 解耦 | 通过 | Canvas 源码、包清单无 Rendering2D 引用；TestProject 独立安装两包。 |
| 世界 Canvas 的 Editor 与 Player 图像 | 通过 | [最终 Editor](evidence/render-canvas-2026-09-26/final-editor-game.png)、[最终 Player](evidence/render-canvas-2026-09-26/final-player.png)；Player 300 帧 `views=1 draws=6`。 |
| Player 点击与 HUD | 通过 | [按钮点击后计数增加](evidence/render-canvas-2026-09-26/final-player-click.png)；样例脚本使用 `SetText`/`SetClass`，RCSS 使用 opacity transition。 |
| Canvas/Camera/Light Gizmo | Canvas 与 Camera 通过；Light 由此前交互实测 | [Canvas 图标选中及边框](evidence/render-canvas-2026-09-26/final-icon-selection.png)；此前在 Rendering2D 样例实测 Light 图标、范围及聚簇选择。图标只在 SceneView，选中时显示边框；Transform 手柄优先于图标。 |
| 虚拟分辨率、Transform 与命中 | 单元测试和当前场景通过；视觉矩阵未全覆盖 | [Inspector 的 800 × 450 设置](evidence/render-canvas-2026-09-26/final-icon-selection.png)；Canvas 测试覆盖旋转、缩放与命中坐标。尚无全部父级变换与不同相机位置的截图矩阵。 |
| 字体切换与字重 | 自动测试通过，Editor 热更新通过 | [none 时无字](evidence/render-canvas-2026-09-26/final-editor-font-none.png)、[B 字体](evidence/render-canvas-2026-09-26/final-editor-font-b.png)、[恢复 A](evidence/render-canvas-2026-09-26/final-editor-font-restored.png)；UI 测试覆盖 A→B→none→A、普通/粗体的字形选择。Player 日志加载 regular/bold。高 DPI 画质仍缺独立截图对比。 |
| 错误 RML 重试 | 通过 | 先前无效 RML 运行 600 帧只产生 1 次导入失败日志和 1 条活动诊断；本次误改 `@font-face` 后也仅有 1 次导入失败日志，修复后文字恢复。日志：`/tmp/inno-testproject-editor-bad-rml.log`、`/tmp/inno-editor-font-audit.log`。 |
| 非 Play 与 Play 输入 | 源码、切换和 Player 交互通过；Editor 点击差异留证不足 | 非 Play GameView 只预览，输入被 suspended；Editor Play 进入/退出无活动错误。Player 的点击计数有图像证据。 |
| 插件普通场景与 `~Samples` | 加载和只读通过 | 普通插件场景曾用临时包打开；Canvas 原始样例 [编辑态](evidence/render-canvas-2026-09-26/final-plugin-sample-edit.png)、[Play 态](evidence/render-canvas-2026-09-26/final-plugin-sample-play.png)、[只读 Inspector](evidence/render-canvas-2026-09-26/final-plugin-sample-readonly.png)；TestProject 300 帧 smoke 退出码 0。 |
| Import Sample | 通过现有自动测试和 CLI 导入 | 发布副本前加载全部已识别资产并编译作者端脚本；失败回滚目录及元数据。有效样例编译通过、无效脚本回滚通过；Canvas 与 Rendering2D 样例均用 CLI 导入过。菜单入口未单独录屏。 |
| 样例不进入 Player | 通过 | 最终 Player 内容包 78 条，仅有 `Inno.GameScripts.dll`、Canvas 与 Rendering2D 插件程序集；无插件原始 `~Samples` 路径或样例程序集。项目中导入的样例副本按普通项目内容处理。 |
| 多 View UI 更新 | 自动测试通过；实际双输出交互覆盖不足 | `IViewContentFrameSource.CompleteFrame` 在所有输出准备后、建图前调用一次；Canvas 同帧汇总输入，6 项 Canvas 测试通过。 |
| 多模型 `RenderOutputRoute` | RenderGraph 测试通过；真实双模型图像未验收 | route 校验模型唯一及内容源唯一；每层独立目标，格式校验后按预乘 Alpha 合成。测试覆盖未配置 route 的冲突诊断、两层 Graph 与非零输出视口。当前 TestProject 只有一个渲染模型。 |
| Material/Shader Preview 与空参数 | 通过 | [顶部独立 Material Preview](evidence/render-canvas-2026-09-26/final-material-inspector.png)、[无参数文案](evidence/render-canvas-2026-09-26/final-material-empty-parameters.png)、[Shader Check 后预览](evidence/render-canvas-2026-09-26/final-shader-preview.png)。 |
| SceneView 缩放保存 | 通过 | [缩放后画面](evidence/render-canvas-2026-09-26/final-scene-zoom.png)；`editor.ini` 的 `navigation.orthographicSize` 在重启后保持测试值。测试完成后恢复原始 `editor.ini`。 |

## 构建、测试与产物

`dotnet` 为 `/Users/aaronliao/.dotnet/dotnet`。命令在各自仓库根目录运行：

| 范围 | 命令 | 结果 |
| --- | --- | --- |
| Engine | `dotnet build InnoEngine.sln -m:1 -p:UseSharedCompilation=false --verbosity quiet` | 0 warning / 0 error；`/tmp/inno-engine-build-final-audit.log`。 |
| Engine | `dotnet test InnoEngine.sln -m:1 --no-build --logger 'console;verbosity=normal'` | 50 程序集，1,297 passed / 0 failed；`/tmp/inno-engine-tests-final-audit.log`。早先有 1 次资产恢复测试偶发失败，单独与完整重跑均通过。 |
| 架构 | `dotnet run --no-build --project tools/Inno.Tooling.Architecture -- .` | 0 violation；`/tmp/inno-arch-final-audit.log`。此前 3,448 项违规已不在当前门禁结果中。 |
| Canvas | `dotnet test Tests/InnoEngine.Canvas.Tests/InnoEngine.Canvas.Tests.csproj --verbosity quiet` | 6 passed / 0 failed；`/tmp/inno-canvas-tests-final-audit.log`。 |
| Rendering2D | `dotnet build InnoProject.sln --verbosity quiet` | 0 warning / 0 error；`/tmp/inno-r2d-build-final-audit.log`。 |
| TestProject | Editor 300 帧 smoke，Player 构建及 300 帧 smoke | 退出码均为 0；Player `views=1 draws=6`；`/tmp/inno-editor-restored-final-smoke.log`、`/tmp/inno-player-build-complete-20260926.log`、`/tmp/inno-player-smoke-complete-20260926.log`。 |
| 插件原始样例 | 临时将 `editor.ini` 指向 `innoengine.canvas::~Samples/SampleScene.iscene`，Editor 300 帧 smoke | 退出码 0；`/tmp/inno-editor-plugin-sample-final-audit.log`。 |

最终 Player 位于 `/tmp/InnoCanvasTestProjectPlayerFinal/TestProject.app`；TestProject 只保留两个独立插件包。测试时临时修改的 RML、场景内存状态及 `editor.ini` 均已恢复；原始场景文件未保存。

## 尚未通过的收口项

1. 用两个真实渲染模型与明确 route 在 Metal 后端保存图层 Alpha/视口的对比截图；当前只有测试模型的 RenderGraph 证据。跨模型不承诺几何深度交错。
2. 保存多相机/多输出输入路由、前后遮挡、父子 Transform 全组合及高 DPI 字形质量的系统画面矩阵。现有单元测试和单场景实测不能代替这些视觉验收。
3. 对 Editor 的 Import Sample 菜单做完整 UI 流程留证。CLI 导入和事务回滚的自动测试已经通过。

这些是验收证据缺口，不把整项计划标成“全部通过”。
