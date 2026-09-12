# Inno.Rendering.Shaders.Tests

[Rendering 索引](README.md) · [Shader 创作 API](Inno.Rendering.Shaders.md) · [Wiki 首页](../README.md)

## 职责与边界

通过公开 API 验证 Shader 源码接口基础及渲染后端注册。测试组合根引用 `Inno.Rendering.Shaders`、
BGFX authoring toolchain、渲染 authoring contract 和 `ModuleHost`。不使用 `InternalsVisibleTo`、
反射穿透或测试专用生产接口，也不创建 GPU 设备。

## 测试入口

- `ShaderSourceFrontendTests`：声明、参数、结构体、数组、宏/include、定位错误、接口一致性与独立语言。
- `ShaderSourceNodeDefinitionTests`：名称稳定的输入/输出端口、聚合成员和不可混淆的类型标识。
- `ShaderSourceFrontendRegistryTests`：TypeRegistry 自动发现、provider 退休、候选冲突释放及旧快照保留。
- `ShaderSourceModuleTests`：跨语言/宏配置接口一致性、完整依赖快照、输入指纹、缺失前端恢复、禁止 main 原型及 retirement barrier。
- `ShaderIrBuilderTests`：中立常量位模式、强类型运算、聚合/矩阵、区域隔离、源码多输出、保留调用副作用及冻结快照。
- `RenderingBackendRegistrationTests`：开放 ID、runtime/authoring 配对及无效注册；不冒充第二个实际 GPU Adapter。
- `ShaderIrStageTests`：阶段接口、绑定唯一性、输出完整性与快照隔离。
- `ShaderTypedNativeCompilationTests`：真实 BGFX shaderc Metal 编译，覆盖运算、私有符号隔离、结构体/固定数组、多输出、uniform/纹理绑定、实例化、MRT、Compute、无能力失败、冻结 include 与原始文件诊断。
- `ShaderGraphLoweringTests`：实际图混合独立节点扩展与源码函数编译；缺失/恢复不丢连接、类型与循环拒绝、聚合冲突、保留副作用、TypeRegistry 候选冲突和 provider 退休。
- `ShaderResourceIrTests`：buffer/image 格式、形状、权限、指令顺序；原生编译覆盖读写/原子加、2D/array/3D image、四类采样和 discard。
- `ShaderControlFlowIrTests`：词法捕获与合并、失败原子回滚、禁止修改父/兄弟/已关闭作用域；具名同时替换状态；嵌套 fragment-only 校验；真实 Metal 分支/循环/storage 与聚合零次循环编译。
- `ShaderGraphProgramTests`：默认图往返与分阶段降低、缺失阶段/输出保留、重复契约和能力诊断、输入重命名与暴露参数同步、未完成输入的原生持久化。

上述 public 方法是 xUnit 发现入口，不是产品 API。2026-09-11 最新检查点 107 项 case 全部通过；其中 15 项调用本机 Metal 原生编译器。
语义指纹用例验证画布位移不失效，源码、嵌套指令、storage 权限、slot 和工作组变化会失效。
矩阵原生编译覆盖 2×2 到 4×4 的全部 9 种形状；一个测试内的多次编译不重复计入 case 数。
`MetalShaderFactAttribute` 在非 macOS 主机明确标为 skipped，不用静默 return 冒充原生验证通过。

```sh
/Users/aaronliao/.dotnet/dotnet test \
  tests/rendering/Inno.Rendering.Shaders.Tests/Inno.Rendering.Shaders.Tests.csproj \
  --no-restore -m:1 -p:UseSharedCompilation=false -v quiet
```

先从主仓库完成 restore；在限制 IPC 的沙箱中运行 MSBuild/VSTest 可能需要相应执行授权。
Fixture 依次释放 Registry、TypeCatalog、ModuleHost；临时目录只属于测试本身。

这些测试不创建 GPU 设备：原生编译通过不是实际 GPU 数值/像素/性能验收；仍不覆盖完整资源布局/屏障、Shader Editor、GPU 反射或真实 collectible 插件的端到端卸载。
后续补充矩阵与本机/GPU 验收见[实施记录](../issues/2026-09-11-unified-shader-implementation.md)。
