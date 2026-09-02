# Inno.Player

[Runtime 索引](README.md) · [Runtime](Inno.Runtime.md) · [Build](../build/Inno.Build.md) · [Assets](../assets/Inno.Assets.md)

`Inno.Player` 是不含 Editor、Build、Compiler、Importer 或 Toolchain 依赖的跨平台 executable composition root。它没有 public API；唯一稳定输入是 Build 写出的 `Content/` 契约。

## 启动顺序

1. 在 Windows executable 旁或 macOS `.app/Contents/Resources` 中定位 `Content`。
2. 从 `runtime.manifest` 读取并验证 Application ID，再将 content-addressed packs 物化到该应用的持久目录。
3. 创建 `EngineHost`，并使用宿主 Serialization generation 解码 `GameRuntimeManifest`。
4. 验证 `Content/Managed/*.dll` 与清单完全一致，通过 `ModuleHost` 在 collectible ALC 中候选加载、依赖排序并原子激活冻结的 Plugin/Game Scripts generation。
5. 创建 `ProjectSettingsStore`，应用依赖有序的 Plugin setting contributions。
6. 创建 `RuntimeSessionKind.Player` Session；它只通过部署 `AssetDatabase` 加载 Artifact。
7. 创建 SDL3 窗口、BGFX device 与 `RenderRuntimeLayer`，并从项目 `GamePresentationSettings` 建立主呈现 viewport。
8. 从只读 Catalog 加载 startup `SceneAsset`，实例化到该 Session 的 `SceneWorld`。
9. 轮询平台事件并调用 `RuntimeSession.Tick`；关闭时逆序释放 Rendering、Session、Settings、Host、GPU、Window 和 Platform。

Player 不扫描 `Plugins/`，不运行 Importer，不启动 watcher，也不尝试从源文件重建缺失 Artifact。部署内容不完整时启动失败，避免在用户机器上生成与导出 generation 不一致的内容。

## 平台布局

```text
macOS:  <Product>.app/Contents/MacOS/<Product>
        <Product>.app/Contents/Resources/Content/...

Windows: <Product>-Windows-x64/<Product>.exe
         <Product>-Windows-x64/Content/...
```

Player Support Pack 在引擎发布阶段预生成；Game Export 只组合目标 Pack 和项目 Artifact，不运行 `dotnet`，也不依赖引擎源码 checkout。

## 部署边界

Player closure 不允许包含 Roslyn、Editor、Build、Scripting Compiler/Reload、Assets Pipeline、Plugins Authoring、shaderc、texturec、C# source、Shader source、裸 Assets 或裸 Plugins。平台原生运行库位于应用布局的 `native/`，只包含执行游戏实际需要的 SDL3/BGFX runtime binary。

```text
Content/
├── catalog.inno
├── content-<hash>.pack
└── runtime.manifest
```

## 生命周期与诊断

- 可写数据严格位于 `LocalApplicationData/InnoEngine/<applicationId>`，不使用产品名或进程名代替稳定 Application ID。
- Managed closure 与清单缺失、多余或重复时拒绝启动；Plugin/Scripting 不会被误归类成默认上下文的 Host assembly。
- 窗口 resize 调整 backbuffer；quit/close event 结束主循环。
- 默认以 `1280×720` 参考帧保持比例：Runtime 把所有模型 Provider 指向同一个居中 content viewport，并在外侧清除纯黑 letterbox/pillarbox。关闭项目的 `Preserve Aspect Ratio` 后使用完整 backbuffer。Game View 消费同一项目设置，因此不是另一套仅供预览的缩放规则。
- Render diagnostics 映射到 Host 的实例 `LogRouter`；启动异常写入标准错误并返回非零退出码。
- Player 不参与 Editor hot reload；每次启动只消费一个完整、不可变的导出 generation，但仍使用 collectible ALC 保持统一生命周期和干净释放。
- `--smoke-frames` 验收模式会报告实际完成的 frame/view/draw/dispatch 数，不再只依赖进程退出码判断启动成功。

[上一页：Runtime](Inno.Runtime.md)
