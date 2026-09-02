# Inno.Native.LibraryLoading

[Native 索引](README.md) · [Wiki 首页](../README.md)

该项目拥有跨平台动态库加载机制，不引用任何上层领域。

## 公开 API

- `NativeDllConstants`：当前 host 的动态库扩展与名称规范。
- `NativeDllLoader`：从明确路径加载 Native library 并解析 export。

调用者必须提供受信任、已验证路径并管理 handle 生命周期。加载失败或 symbol 缺失明确抛出；没有系统范围 fallback 搜索。
