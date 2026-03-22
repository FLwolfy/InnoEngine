using System;

namespace Inno.Platform.Graphics;

public struct ResourceSetBinding()
{
    // Shaders
    public ShaderStage shaderStages;
    
    // Uniforms
    public IUniformBuffer[] uniformBuffers = [];
    
    // Textures
    public ITexture[] textures = [];
    public ISampler[] samplers = [];
}

public interface IResourceSet : IDisposable;