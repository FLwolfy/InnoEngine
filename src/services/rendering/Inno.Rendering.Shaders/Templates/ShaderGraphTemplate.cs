using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Contributes an ordinary shader graph to the shared asset creation workflow.</summary>
public abstract class ShaderGraphTemplate
{
    /// <summary>Gets the stable creation identity, independent of its display name.</summary>
    public abstract string id { get; }
    /// <summary>Gets the user-facing menu label.</summary>
    public abstract string displayName { get; }
    /// <summary>Creates a fresh detached graph with its target and parameter declarations.</summary>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>A graph ready for native serialization and ordinary shader import.</returns>
    public abstract GraphDocument Create(SerializationRegistry serialization, SerializationContext context);
}

internal sealed class RasterShaderGraphTemplate : ShaderGraphTemplate
{
    public override string id => "inno.shader.raster";
    public override string displayName => "Raster";
    public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
        => ShaderGraphTemplates.CreateRaster(serialization, context);
}
