using System;
using Inno.Platform.Graphics;

namespace Inno.Engine.Graphics.Resources.CpuResources;

public class Shader(Guid guid, string name, ShaderStage stage, byte[] shaderBinaries)
{
    public Guid guid { get; } = guid;
    public string name { get; } = name;
    public ShaderStage stage { get; } = stage;
    public byte[] shaderBinaries { get; } = shaderBinaries;
}