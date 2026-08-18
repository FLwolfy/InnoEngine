# Inno.Core.Events

[上一页：Framework](Inno.Core.Framework.md) · [Core 索引](README.md) · [下一页：Coroutines](Inno.Core.Coroutines.md)

Events 系统由一个 `EventDispatcher` 和多个有序 `EventHub` 构成。Dispatcher 决定 hub 顺序；Hub 决定监听器优先级，并支持只终止当前 hub 或终止全局链。

## 分发模型

- Hub `order` 越大越先执行；相同 order 按 hub 创建顺序。
- 同一 Hub 内 listener `priority` 越大越先执行；相同 priority 按注册顺序。
- 监听具体事件类型时，分发还会沿事件基类向上匹配。
- `HandleInHub()` 停止当前 Hub 剩余 listener，但后续 Hub 仍收到事件。
- `HandleInGlobal()` 停止当前 Hub 和后续全部 Hub。

## EventDispatcher

| 方法 | 说明 |
| --- | --- |
| `CreateHub(int order = 0)` | 创建并附加一个 Hub。 |
| `Enqueue(Event)` | 线程安全地排队，等待 `Flush()`。 |
| `Flush()` | 排空调用时可见的队列并逐个 `Emit`。 |
| `Emit(Event)` | 立即按 order 分发到全部有效 Hub。 |

## EventHub

| 成员 | 说明 |
| --- | --- |
| `order` | 可动态修改；Dispatcher 会重新排序。 |
| `isValid` | 尚未 Dispose、仍连接活 dispatcher。 |
| `Listen<TEvent>(Action<TEvent>, priority)` | 添加长期监听，返回 dispose 即退订的 token。 |
| `ListenOnce<TEvent>(...)` | 首次调用后自动退订，也可提前 dispose。 |
| `Announce(Event)` | 仅在该 Hub 内立即广播，不走 Dispatcher 链。 |
| `Dispose()` | 清空监听并从 Dispatcher 移除。 |

```csharp
EventDispatcher dispatcher = new();
using EventHub ui = dispatcher.CreateHub(order: 100);
using EventHub game = dispatcher.CreateHub(order: 0);

ui.Listen<KeyPressedEvent>(e =>
{
    if (e.key == KeyCode.Escape)
        e.HandleInHub();
});

game.Listen<KeyEvent>(e => Console.WriteLine(e.key));
dispatcher.Enqueue(new KeyPressedEvent(1, KeyCode.Space));
dispatcher.Flush();
```

`HandleInHub()` 只能在该事件当前正在 Hub dispatch 的调用栈中使用，其他位置调用会抛 `InvalidOperationException`。

## 内置事件类型

所有事件派生自 `Event`，构造参数也会作为只读属性公开。

| 分类 | 类型 | 数据 |
| --- | --- | --- |
| Application | `ApplicationEvent` | 抽象基类 |
| Application | `ApplicationQuitEvent` | 无附加数据 |
| Keyboard | `KeyEvent` | `windowId`、`key`、`modifiers` |
| Keyboard | `KeyPressedEvent` | 另有 `repeat` |
| Keyboard | `KeyReleasedEvent` | 基类数据 |
| Mouse | `MouseEvent` | `windowId` |
| Mouse | `MouseMovedEvent` | `x`、`y` |
| Mouse | `MouseScrolledEvent` | `offsetX`、`offsetY` |
| Mouse | `MouseButtonEvent` | `button` |
| Mouse | `MouseButtonPressedEvent` | 基类数据 |
| Mouse | `MouseButtonReleasedEvent` | 基类数据 |
| Window | `WindowEvent` | `windowId` |
| Window | `WindowResizeEvent` | `width`、`height` |
| Window | `WindowCloseEvent` | 基类数据 |

Keyboard/Mouse 枚举详见 [Inno.Core.Input](Inno.Core.Input.md)。

## 生命周期与热重载

subscription token、Hub listener delegate 都会强引用处理对象。插件或脚本实例卸载前必须 dispose 订阅；`Layer` 帮助自动处理其通过 protected API 建立的订阅。外部直接注册的 listener 则由调用者负责清理。
