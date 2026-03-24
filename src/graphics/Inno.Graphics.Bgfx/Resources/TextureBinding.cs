using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

internal readonly record struct TextureBinding(int slot, bgfx.UniformHandle uniform, BgfxTexture texture);
