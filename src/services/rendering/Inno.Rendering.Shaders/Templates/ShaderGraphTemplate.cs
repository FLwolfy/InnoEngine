using System;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Declares immutable creation metadata for a Shader graph template.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShaderGraphTemplateAttribute : Attribute
{
    /// <summary>Creates Shader graph template discovery metadata.</summary>
    /// <param name="id">Stable template identity used by creation commands.</param>
    /// <param name="displayName">User-facing creation menu label.</param>
    public ShaderGraphTemplateAttribute(string id, string displayName)
    {
        this.id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Shader graph template identity cannot be empty.", nameof(id))
            : id;
        this.displayName = string.IsNullOrWhiteSpace(displayName)
            ? throw new ArgumentException("Shader graph template display name cannot be empty.", nameof(displayName))
            : displayName;
    }

    /// <summary>Gets the stable template identity used by creation commands.</summary>
    public string id { get; }

    /// <summary>Gets the user-facing creation menu label.</summary>
    public string displayName { get; }
}

/// <summary>Contributes an ordinary shader graph to the shared asset creation workflow.</summary>
public abstract class ShaderGraphTemplate
{
    /// <summary>Creates a fresh detached graph with its target and parameter declarations.</summary>
    /// <param name="serialization">Current owner converters.</param>
    /// <param name="context">Complete owner reference context.</param>
    /// <returns>A graph ready for native serialization and ordinary shader import.</returns>
    public abstract GraphDocument Create(SerializationRegistry serialization, SerializationContext context);
}

[ShaderGraphTemplate(ShaderBuiltInIds.rasterTemplate, "Raster")]
internal sealed class RasterShaderGraphTemplate : ShaderGraphTemplate
{
    public override GraphDocument Create(SerializationRegistry serialization, SerializationContext context)
        => ShaderGraphTemplates.CreateRaster(serialization, context);
}

internal static class ShaderBuiltInIds
{
    internal const string rasterTemplate = "inno.shader.raster";
}
