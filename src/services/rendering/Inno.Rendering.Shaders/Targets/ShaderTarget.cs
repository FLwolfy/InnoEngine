using System;
using System.Threading;
using Inno.Core.Graphs;
using Inno.Core.Serialization;

namespace Inno.Rendering.Shaders;

/// <summary>Declares the immutable identity used to select a Shader Target from an authored graph.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class ShaderTargetAttribute : Attribute
{
    /// <summary>Creates target discovery metadata.</summary>
    /// <param name="id">Stable target identity persisted by Shader assets.</param>
    public ShaderTargetAttribute(string id)
        => this.id = string.IsNullOrWhiteSpace(id)
            ? throw new ArgumentException("Shader target identity cannot be empty.", nameof(id))
            : id;

    /// <summary>Gets the stable target identity persisted by Shader assets.</summary>
    public string id { get; }
}

/// <summary>Expands a domain's surface contract into ordinary graph stages before source dependency capture.</summary>
public abstract class ShaderTarget
{
    /// <summary>Builds explicit stage interfaces, resource declarations, techniques and pass states.</summary>
    /// <param name="context">Invocation-scoped detached document and complete owner serialization services.</param>
    /// <param name="cancellationToken">Cancellation between expansion operations.</param>
    /// <returns>A detached graph for the common typed compiler; all required capabilities belong in its pass contracts.</returns>
    public abstract GraphDocument Expand(ShaderTargetContext context, CancellationToken cancellationToken);
}

/// <summary>Supplies neutral authoring data to a domain target without exposing an adapter or live asset.</summary>
public sealed class ShaderTargetContext
{
    internal ShaderTargetContext(GraphDocument document, SerializationRegistry serialization, SerializationContext references)
    { this.document = document; this.serialization = serialization; this.references = references; }

    /// <summary>Gets a private document copy; expansion cannot modify the authored source.</summary>
    public GraphDocument document { get; }
    /// <summary>Gets the current owner converter registry, valid only during expansion.</summary>
    public SerializationRegistry serialization { get; }
    /// <summary>Gets the complete owner reference context used for generated resource defaults.</summary>
    public SerializationContext references { get; }
}
