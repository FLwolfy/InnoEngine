# Inno.Adapter.Text.FreeTypeHarfBuzz

[Text 索引](README.md) · [Native Text](../native/Inno.Native.Text.md)

默认 Text backend 用 HarfBuzz 做 Unicode shaping，用 FreeType 提取字形 bitmap。具体 ABI 限定在此 adapter 与 `Inno.Native.Text`，Session/脚本不接触原生指针。后端对字体 bytes 创建/缓存 face，并按运行期所有权释放。

公开 `FreeTypeHarfBuzzTextBackend()` 初始化一个原生 context，并实现 `ITextBackend` 的 `LoadFont`、`ReleaseFont`、`Shape`、`Rasterize`、`Dispose`。输入字节复制/固定在调用边界，结果转换为中立 `TextLayout`/`GlyphBitmap`。构造、数据校验或 native 调用失败时抛出明确异常；`Dispose` 幂等，之后的方法拒绝访问。

## 使用与边界

Host 由 [默认 adapter catalog](../runtime/Inno.Adapter.Default.md) 创建此 backend，再交给 `TextRuntime` 持有。`LoadFont(ReadOnlySpan<byte>, faceIndex)` 允许 OpenType collection 中的显式 face；`Shape` 处理 Unicode、语言/script/方向，`Rasterize` 输出 8-bit coverage，而不是 GPU texture。`ReleaseFont` 后或跨 backend 使用旧 handle 会失败。

```csharp
using Inno.Adapter.Text.FreeTypeHarfBuzz;
using Inno.Text;

using var backend = new FreeTypeHarfBuzzTextBackend();
TextFontHandle face = backend.LoadFont(fontBytes, 0);
TextLayout layout = backend.Shape(face, "界面", new TextStyle(24f), default);
backend.ReleaseFont(face);
```

此代码属于 Host/adapter 测试层；游戏脚本应使用 `InnoEngine.Text.Text`。详情参见 [Text Runtime](Inno.Text.Runtime.md)。
