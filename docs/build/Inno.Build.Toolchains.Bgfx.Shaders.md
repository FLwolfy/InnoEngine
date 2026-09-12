# Inno.Build.Toolchains.Bgfx.Shaders

[Build 索引](README.md) · [Wiki 首页](../README.md) · [BGFX 工具链](Inno.Build.Toolchains.Bgfx.Tools.md) · [Shader 创作](../render/Inno.Rendering.Shaders.md)

## 职责

离线 Shader 图编译命令。使用普通 Asset Pipeline 导入 `.ishader` 及 `.ishadersource`，经统一图降低、typed IR、BGFX 生成与原生编译输出 `RenderShaderArtifactCodec` 产物。当前源码导入或编译错误使命令失败，不使用旧成功产物掩盖构建错误。

该可执行项目没有供脚本调用的 public/protected 扩展 API。语言与节点扩展属于 Inno.Rendering.Shaders，后端生成属于 Inno.Build.Toolchains.Bgfx.Tools。

## 用法

```sh
dotnet Inno.Build.Toolchains.Bgfx.Shaders.dll \
  /absolute/asset-root ImGui.ishader MacOSArm64 Metal /absolute/output/ImGui.bin
```

参数依次为资产根目录、Shader 源内路径、编译平台、GraphicsApi、产物路径。当前 CLI 使用空变体；需要多变体分发时由正式内容构建管线收集请求。

## 内置 ImGui

`Inno.Adapter.Presentation.ImGui.Bgfx` 的构建目标调用本 CLI 编译内置图并嵌入平台/API 对应产物。Editor 启动只读取匹配资源，不启动旧完整源码编译旁路；缺少对应资源明确报告安装/构建错误。

本机已完成 Metal/Vulkan/OpenGL 产物编译以及 Metal Editor 启动检查；这不代表 Vulkan GPU 执行或 Windows 后端验收。Windows 按用户要求暂缓。

临时导入缓存与当前进程绑定并在退出时释放。图/源码仍属于创作输入，不能将本 CLI、图解析器或 Editor 项目打入 Player。
