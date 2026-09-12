# Inno.Build.Toolchains.Bgfx.Tools

[Build 索引](README.md) · [Rendering Assets](../render/Inno.Rendering.Assets.md)

## 公开 API

- `BgfxShadercToolchain`、`BgfxShaderTargetPlatform`：把共享 Shader IR 编译为目标 backend artifact。
- `BgfxTextureTargetCompiler`：把创作纹理离线编译为 portable KTX。
- `BgfxGameContentCompiler`：遍历目标构建 snapshot 并写入 `TargetArtifacts`。
- `BgfxShaderSourceFrontend`：实现 `IShaderSourceFrontend`；`languageId` 为
  `inno.shader-language.bgfx-sc`，`Analyze(ShaderSourceRequest)` 返回函数接口、原始 include 依赖和定位诊断。

这些类型只在 authoring/build 路径使用。Player 通过 `FileRenderTargetArtifactProvider` 读取结果，不引用本项目或 BGFX tools。

## 源码函数前端

定义和原型都禁止 `main()`；resolver 的共同 retirement barrier 原样传播，不能降格成普通 include 诊断。
跨实现/变体接口一致性与完整输入快照由 [`ShaderSourceFrontendCatalog.AnalyzeModule`](../render/Inno.Rendering.Shaders.md)
负责，BGFX 前端只分析其明确给定的一个配置。

前端包含词法、条件预处理与声明分析，不使用正则匹配函数签名。当前已覆盖注释、宏替换和别名重扫描、
token 拼接、include guard/pragma once、常量表达式、结构体、typedef、固定数组、函数声明与
`in/out/inout`。重载歧义、未知类型、非法数组和不平衡声明返回原始文件位置，不猜测接口。

函数体作边界与隐式阶段访问检查，完整合法性由 shaderc 检查；前端已接到新的 typed stage 编译，但资产替换尚未完成。
完整 BGFX 原生头文件、所有 storage/image 类型和语言扩展的覆盖仍待后续集成验证，不能把接口单元测试
当作真实 GPU 编译结果。扩展接口见 [Inno.Rendering.Shaders](../render/Inno.Rendering.Shaders.md)。

源码模块禁止非 const 全局绑定；阶段输入、varying 和实际资源槽由图目标/后端拥有。
源码不得隐式读取 `gl_*`、默认阶段 attribute/varying、预定义矩阵或生成绑定名，必须通过公开函数参数传入。
结构成员不被误判为阶段全局变量；目标语言的 native 保留名字不作为模块的局部变量名字使用。
这种职责划分也符合 BGFX 对阶段入口的要求。Windows D3D 编译需在 Windows 执行，不能由 macOS
结果替代：[BGFX shaderc 文档](https://bkaradzic.github.io/bgfx/tools.html#building-shaders)。

## Typed stage 编译

`BgfxShadercToolchain.supportedSourceLanguages` 声明支持的语言；
`CompileAsync(ShaderStageToolRequest, CancellationToken)` 消费 `ShaderIrStage`，生成入口/接口/绑定与源码函数调用，
复用现有 shaderc 进程执行器，返回不可变 bytes、逻辑到 native 绑定名及结构化诊断。核心不保存 SC 表达式。

- 私有函数/常量按冻结模块内容隔离；结构体按完整布局共享 native 类型，普通运算与函数调用共用值流。
- 只读取已冻结 include 解析边，不在后台重新使用活动 resolver 或猜测 include 别名。
- 无 varying 的阶段写入明确的无声明文件，避免 shaderc 对空文件误报警；不是插入伪造 input。
- shaderc 预处理会去掉 `#line`。此 Adapter 通过保留源码标记及原生错误代码摘录，恢复原始文件/行；无法精确映射的诊断保留 generated 位置，不伪造源行。
- 不把 exit code 0 当成唯一成功条件；原生明确 ERROR 或缺失 bytes 一样拒绝候选。
- 原生 stdout/stderr 的格式解析只在 BGFX 工具链；公共 `ShaderCompiler` 仅消费结构化 `ShaderDiagnostic`，不再按 shaderc 行格式做正则判断。
- 支持 typed 纹理采样、显式 LOD、discard、buffer load/store/atomic-add 以及 2D/array/3D image load/store。
  BGFX 当前运行时只在 compute 暴露 storage 绑定，因此该限制由 Adapter 明确诊断；能力位、格式访问权限和 slot 上限均在调用 shaderc 前检查。
- Buffer 当前接受明确 stride 的 32-bit 标量、2/4 分量向量；任意结构体 packing、资源函数参数解析与屏障仍需完整 Target/源码前端接线，不能按主机结构体布局猜测。
- Branch/Loop 生成真实控制流，区域内的内存操作不提升到外部；循环先捕获全部 carried state 再同时替换，保证交换值等操作的含义。
- IR 矩阵统一按列构造/提取，Adapter 根据 `BGFX_SHADER_MATRIX_COLUMN_MAJOR` 映射下标和非方阵形状，不能直接把 SC/HLSL 行索引当作列。
- 当前 BGFX uniform storage 为 vec4/mat3/mat4 及固定数组，标量参数须由 Target 做显式 packing/component lowering。
- 数组 typedef、全部原生语法/高级资源及完整反射覆盖仍待补齐；明确错误不是兼容旁路，也不代表完整计划已完成。
