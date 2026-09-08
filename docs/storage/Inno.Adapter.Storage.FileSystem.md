# Inno.Adapter.Storage.FileSystem

## IO 与关闭竞态

直接 Dispose 与文件 IO 在同一个 gate 串行化，不能在 Write 的 finally Release 之前销毁 SemaphoreSlim。已有 waiter 可以观察 disposed 状态并退出；未请求 OS wait handle 的托管 semaphore 随最后的 waiter 回收。写入在原子替换前再次检查取消。Runtime owner 通常会先取消并排空操作，再调用 adapter Dispose。

[Storage 索引](README.md) · [Runtime](Inno.Storage.Runtime.md) · [Wiki 首页](../README.md)

`FileSystemApplicationStorage` 是默认本地 adapter。构造时固定绝对 sandbox root；每次操作重新验证现有路径组件，拒绝 symlink/reparse point 越界。

写入先在目标目录创建唯一临时文件，flush 后执行同目录 replace；失败或取消会清理临时文件。查询与修改通过实例 gate 串行化，列表使用稳定 ordinal key 顺序。Dispose 后全部 API 明确失败。

机器路径只存在于 Composition Root；脚本和 Mechanism 始终只依赖 `StorageKey` 与 `IApplicationStorage`。
