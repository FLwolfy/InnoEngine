# Inno.Core.Identity

[上一页：Job](Inno.Core.Job.md) · [Core 索引](README.md) · [下一页：Input](Inno.Core.Input.md)

Identity 模块为引擎对象提供两个层次的身份：跨保存/重载保持的 `persistentId`，以及只在当前 Registry 注册期间有效的 `runtimeId`。对象通过 `IIdentityObject` 获得弱关联的身份存储，无需继承特定基类。

## Identity

`Identity` 是可复制 struct：

| 成员 | 说明 |
| --- | --- |
| `Identity(Guid persistentId)` | 创建未注册的 identity。 |
| `persistentId` | 持久 Guid；注册时为空会生成新值。 |
| `int? runtimeId` | Registry 仍存活且绑定有效时返回 packed runtime ID，否则 null。 |

复制 Identity 只是复制快照；runtime 有效性仍由对 Registry 的弱引用验证。

## IIdentityObject

接口提供默认实现：

- `GetIdentity()`：首次调用会生成一个 persistent ID，之后返回当前 identity 值。
- `protected internal SetIdentity(Identity)`：供派生/引擎基础设施替换关联 identity。

```csharp
public sealed class RuntimeResource : IIdentityObject
{
}

RuntimeResource resource = new();
Guid persistentId = resource.GetIdentity().persistentId;
```

对象到 identity 的映射使用 `ConditionalWeakTable`，不会仅因身份系统而阻止对象 GC。

## IdentityManager

| 成员 | 说明 |
| --- | --- |
| `isInitialized` | 当前全局 Registry 是否初始化。 |
| `ObjectUnregistered` | 对象从 Registry 永久移除后触发；所有 handler 都执行，失败聚合。 |
| `Initialize()` | 创建新 Registry；也可用于 reset。 |
| `Shutdown()` | 丢弃当前 Registry 并标为未初始化。 |
| `Register(obj, Guid? override = null)` | 绑定 runtime ID；已注册返回 `false`。 |
| `InitializePersistentIdentity(obj, Guid)` | 给未注册对象指定非空 persistent ID。 |
| `Unregister(obj)` | 移除 runtime 映射并保留 persistent ID。 |
| `Get<TIdentity>(int runtimeId)` | 按 runtime ID 查找，类型不匹配/陈旧时 null。 |
| `Get<TIdentity>(Guid persistentId)` | 按 persistent ID 查找。 |

```csharp
IdentityManager.Initialize();

RuntimeResource resource = new();
IdentityManager.InitializePersistentIdentity(resource, savedId);
IdentityManager.Register(resource);

int runtimeId = resource.GetIdentity().runtimeId!.Value;
RuntimeResource? same = IdentityManager.Get<RuntimeResource>(runtimeId);

IdentityManager.Unregister(resource);
IdentityManager.Shutdown();
```

## 冲突与陈旧 ID

- 两个不同存活对象不能注册相同 persistent ID；冲突抛 `InvalidOperationException`。
- runtime ID 由 slot + generation 编码。对象移除后 slot 可复用，但旧 ID 的 generation 不匹配，因此不会解析到新对象。
- `InitializePersistentIdentity` 只允许未注册对象；空 Guid、null 或已注册状态会失败。
- Unregister event 在 Registry 已更新后触发，即使 handler 抛错也不会恢复对象。

## 热重载用法

原位替换脚本组件时，应把 persistent ID 迁移到新实例，再注册新实例。外部引用优先保存 persistent ID，不应保存裸 runtime ID 或旧实例引用。
