using System;
using System.Collections.Generic;

namespace Inno.Platform.Graphics;

public enum PrimitiveTopology
{
    TriangleList,
    TriangleStrip,
    LineList,
    LineStrip
}

public enum DepthStencilState
{
    Disabled,
    DepthOnlyLessEqual,
    DepthOnlyGreaterEqual,
    DepthReadOnlyLessEqual,
    DepthReadOnlyGreaterEqual
}

public enum BlendMode
{
    Opaque,
    AlphaBlend,
    Additive,
    Override
}

public struct PipelineStateDescription
{
    public IShader vertexShader;
    public IShader fragmentShader;
    public List<Type> vertexLayoutTypes;
    
    public BlendMode blendMode;
    public PrimitiveTopology primitiveTopology;
    public DepthStencilState depthStencilState;
    
    public ResourceSetBinding[]? resourceLayoutSpecifiers;
}

public interface IPipelineState : IDisposable;
