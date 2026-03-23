using Inno.Graphics;

namespace Inno.Graphics;

/// <summary>
/// Represents a compiled shader object.
/// </summary>
public interface IGraphicsShader : IGraphicsResource
{
}

/// <summary>
/// Represents a linked shader program.
/// </summary>
public interface IGraphicsProgram : IGraphicsResource
{
}

/// <summary>
/// Represents a vertex input layout object.
/// </summary>
public interface IGraphicsInputLayout : IGraphicsResource
{
}

/// <summary>
/// Represents an immutable render pipeline object.
/// </summary>
public interface IGraphicsRenderPipeline : IGraphicsResource
{
}
