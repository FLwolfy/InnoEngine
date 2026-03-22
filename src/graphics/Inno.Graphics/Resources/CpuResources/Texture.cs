using System;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.CpuResources;

public class Texture(
    Guid guid,
    string name,
    int width,
    int height,
    byte[] data,
    PixelFormat format,
    TextureUsage usage,
    TextureDimension dimension
) {
    public Guid guid { get; } = guid;

    public string name { get; } = name;
    public int width { get; } = width;
    public int height { get; } = height;
    public byte[] data { get; } = data;

    public PixelFormat format { get; } = format;
    public TextureUsage usage { get; } = usage;
    public TextureDimension dimension { get; } = dimension;
}