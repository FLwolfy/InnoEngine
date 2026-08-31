# Inno.Core.Input

[上一页：Identity](Inno.Core.Identity.md) · [Core 索引](README.md) · [下一页：Diagnose](Inno.Core.Diagnose.md)

Input 项目只定义跨平台输入语义枚举，不维护按键状态。状态采集由 Platform 层完成，事件传递参见 [Inno.Core.Events](Inno.Core.Events.md)。

## KeyCode

| 分组 | 值 |
| --- | --- |
| 未知 | `Unknown` |
| 字母 | `A`–`Z` |
| 主键盘数字 | `D0`–`D9` |
| 常用控制 | `Escape`, `Space`, `Enter`, `Tab`, `Backspace` |
| 方向 | `LeftArrow`, `UpArrow`, `RightArrow`, `DownArrow` |
| 修饰键实体 | `LeftSuper`, `RightSuper`, `LeftShift`, `RightShift`, `LeftCtrl`, `RightCtrl`, `LeftAlt`, `RightAlt` |
| 编辑/导航 | `CapsLock`, `Insert`, `Delete`, `Home`, `End`, `PageUp`, `PageDown` |
| 数字键盘 | `NumPad0`–`NumPad9`, `NumLock` |
| 锁定 | `ScrollLock` |
| 功能键 | `F1`–`F12` |
| 标点 | `Plus`, `Comma`, `Minus`, `Period`, `Slash`, `Tilde`, `Backslash`, `Semicolon`, `Quote`, `LeftBracket`, `RightBracket` |

枚举数值接近常见虚拟键码，但业务代码应比较枚举名，不要假定所有平台都直接提供相同整数。

## KeyModifier

`[Flags]` enum：`None`、`Alt`、`Control`、`Shift`、`Super`。

```csharp
bool saveShortcut = e.key == KeyCode.S &&
    (e.modifiers & KeyModifier.Control) != 0;
```

macOS Command / Windows key 等平台主修饰键可映射到 `Super`。`KeyModifier` 表示事件发生时的组合状态，与 `KeyCode.LeftCtrl` 这种具体实体键不同。

## MouseButton

`Left`、`Right`、`Middle`、`XButton1`、`XButton2`。

## MouseCursor

| 值 | 用途 |
| --- | --- |
| `None` | 隐藏/不设置光标。 |
| `Arrow` | 默认箭头。 |
| `TextInput` | 文本输入 I-beam。 |
| `ResizeAll` | 全方向移动/缩放。 |
| `ResizeNS`, `ResizeEW` | 垂直、水平 resize。 |
| `ResizeNESW`, `ResizeNWSE` | 两种对角 resize。 |
| `Hand` | 可点击链接/抓手。 |
| `NotAllowed` | 禁止操作。 |

## 事件示例

```csharp
hub.Listen<MouseButtonPressedEvent>(e =>
{
    if (e.button == MouseButton.Left)
        BeginSelection();
});

hub.Listen<KeyPressedEvent>(e =>
{
    if (e.key == KeyCode.F5 && !e.repeat)
        StartGame();
});
```
