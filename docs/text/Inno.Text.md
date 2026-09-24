# Inno.Text

[Text 索引](README.md) · [UI](../ui/Inno.UI.md)

后端中立契约位于 `src/services/text/Inno.Text`。`FontAsset` 保存可恢复资产身份与导入 metadata；`TextStyle`、`TextShapingOptions` 描述字号、字重、方向、语言等输入。`TextLayout` 返回字形与位置，`GlyphBitmap` 返回像素和度量，不包含 GPU、Scene 或 RmlUi 对象。

`ITextService.Shape` 与 `Rasterize` 是实际能力；脚本通过 `Text` 门面在活动 `TextExecutionContext` 内调用。没有 Session scope 会明确失败。唯一 `Properties/ScriptingApi.cs` 声明逻辑 `InnoEngine.Text` 暴露面；不导出 `ITextBackend`、native handle 或 adapter。

该层只依赖 Assets 与 Foundation；绘制、字形图集、Canvas 组件仍属于消费者。

## 公开类型与使用

| 类型 | 语义 |
| --- | --- |
| `FontAsset`、`FontMetadata`、`FontMetadataCodec` | 字体资产、face 数/编码长度与严格 runtime metadata 编解码；Missing 或无 payload 不会静默变成默认字体。 |
| `TextStyle`、`TextShapingOptions`、`TextDirection`、`TextFontStyle` | 字号、face、字重、字间距、语言/script/方向输入；构造时校验范围及有限浮点。 |
| `TextGlyph`、`TextMetrics`、`TextLayout`、`GlyphBitmap` | 不可变 shaping/光栅化结果，字形 ID 可用于后续 `Rasterize`。 |
| `ITextService`、`Text`、`TextExecutionContext` | 显式服务、脚本门面与严格 LIFO 的 Session 作用域。 |
| `ITextBackend`、`TextFontHandle` | Host/adapter 的低层 face 所有权协议，不属于逻辑脚本 API。 |

```csharp
using InnoEngine.Text;

static TextLayout LayoutTitle(FontAsset font)
    => Text.Shape(font, "你好 Inno", new TextStyle(24f));
```

在活动 Session 帧中调用；`TextLayout.glyphs` 的 ID 与 FontAsset/face 对应，跨后端代际不可直接复用。Text 本身不返回 GPU 纹理或 Scene 组件。
