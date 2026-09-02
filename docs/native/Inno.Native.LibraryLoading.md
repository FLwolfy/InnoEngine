# Inno.Native.LibraryLoading

[Native 索引](README.md) · [Wiki 首页](../README.md)

该项目拥有跨平台动态库加载机制，不引用任何上层领域。

## 公开 API

- `NativeDllConstants`：当前 host 的动态库扩展与名称规范。
- `NativeDllLoader`：从明确路径加载 Native library 并解析 export。

调用者必须提供受信任、已验证路径并管理 handle 生命周期。加载失败或 symbol 缺失明确抛出；没有系统范围 fallback 搜索。

开发环境中，`EnsureNativeFile` 会把仓库 `.lib` 目录中的当前产物与应用输出目录按 SHA-256
内容身份比较，仅在内容变化时原子覆盖部署副本。该判断不依赖长度或时间戳，因此重新构建出同尺寸
二进制时也不会继续加载陈旧副本。Support Pack/Player 没有源码 checkout 时只读取已冻结的部署产物。
