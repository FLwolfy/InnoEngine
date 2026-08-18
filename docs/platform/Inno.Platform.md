# Inno.Platform

[Platform 索引](README.md) · [Wiki 首页](../README.md)

`Inno.Platform` 提供窗口与事件循环，并通过一个窄的公开扩展契约连接 ImGui 等可选后端。`Inno.Platform.ImGui` 不再依赖 `InternalsVisibleTo` 或 Platform 的 internal SDL hook。

## PlatformApplication

| 成员 | 说明 |
| --- | --- |
| 构造函数 | 初始化 SDL3 Video/Events。 |
| `CreateWindow(options)` | 创建并拥有一个窗口。 |
| `PollEvent(out Event?)` | 轮询、合并并返回下一个引擎事件。 |
| `GetWindows()` | 返回当前有效窗口，包括集成创建的 viewport window。 |
| `RegisterExtension(extension)` | 注册当前 application 的后端扩展，返回可释放 registration。 |
| `Dispose()` | 通知扩展、释放窗口并关闭平台后端。 |

## IPlatformApplicationExtension

该接口用于平台实现集成，不属于游戏脚本 API：

- `ProcessNativeEvent(...)`：引擎翻译事件前接收短生命周期 native event。
- `RenderLiveResizeWindow(...)`：macOS 等 live-resize 循环中重绘集成窗口。
- `OnApplicationDisposing(...)`：平台资源销毁前释放 application-bound 状态。

`PlatformNativeEvent.data` 是不透明指针，只在回调期间有效，不得缓存。`backendName` 当前为 `SDL3`；扩展必须先检查名字再解释指针。

## PlatformWindow 与 native handle

窗口公开 `windowId`、`title`、`width`、`height`、`isClosed`、`RequestClose()`、`Dispose()`。`nativeHandles` 除操作系统窗口/display handle 外，还提供：

- `backendName`：当前窗口后端名。
- `backendWindowHandle`：后端自己的不透明窗口 handle。

后端集成可以据此构造自己的 native wrapper，普通业务代码不应持有该 handle。

## 注册示例

```csharp
sealed class BackendExtension : IPlatformApplicationExtension
{
    public void ProcessNativeEvent(
        PlatformApplication application,
        PlatformNativeEvent nativeEvent)
    {
        if (nativeEvent.backendName != "SDL3")
            return;
        // Interpret nativeEvent.data only during this call.
    }

    public void RenderLiveResizeWindow(
        PlatformApplication application,
        uint windowId)
    {
    }

    public void OnApplicationDisposing(PlatformApplication application)
    {
    }
}

using IDisposable registration = application.RegisterExtension(new BackendExtension());
```
