using System;

namespace Inno.Rendering.Shaders;

/// <summary>Identifies an authored target that is absent from the current extension generation.</summary>
public sealed class ShaderTargetUnavailableException : InvalidOperationException
{
    /// <summary>Creates a missing-target diagnostic without retaining extension objects.</summary>
    /// <param name="targetId">The stable authored target identity.</param>
    public ShaderTargetUnavailableException(string targetId)
        : base($"Shader target '{targetId}' is unavailable. The authored graph is retained for recovery.")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetId);
        this.targetId = targetId;
    }

    /// <summary>Gets the stable target identity required by the authored graph.</summary>
    public string targetId { get; }
}
