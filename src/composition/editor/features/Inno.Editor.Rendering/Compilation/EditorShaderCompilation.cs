using System;
using System.Collections.Generic;
using Inno.Rendering;

namespace Inno.Editor.Rendering;

/// <summary>Identifies native compilation state independently of document persistence.</summary>
public enum EditorShaderCompilationState
{
    /// <summary>The latest semantic candidate is being compiled.</summary>
    Compiling,
    /// <summary>The latest semantic candidate compiled successfully.</summary>
    Succeeded,
    /// <summary>The latest semantic candidate failed.</summary>
    Failed
}

/// <summary>Contains detached compiler state without retaining assets, tasks or extension instances.</summary>
/// <param name="state">Latest requested semantic candidate state.</param>
/// <param name="usingLastGood">Whether rendering still uses a previous successful candidate.</param>
/// <param name="diagnostics">Immutable latest completed compiler diagnostics.</param>
public sealed record EditorShaderCompilationSnapshot(EditorShaderCompilationState state, bool usingLastGood,
    IReadOnlyList<ShaderDiagnostic> diagnostics);

/// <summary>Schedules Editor shader compilation for the active device without exposing native handles to authoring panels.</summary>
public sealed class EditorShaderCompilation
{
    private readonly EditorRenderTargetArtifactProvider m_provider;
    private readonly GraphicsCapabilities m_capabilities;
    /// <summary>Pairs the authoring artifact owner with the active device's immutable capability snapshot.</summary>
    /// <param name="provider">Host-owned shader compilation service.</param>
    /// <param name="capabilities">Current device capability snapshot.</param>
    public EditorShaderCompilation(EditorRenderTargetArtifactProvider provider, GraphicsCapabilities capabilities)
    { m_provider = provider ?? throw new ArgumentNullException(nameof(provider)); m_capabilities = capabilities ?? throw new ArgumentNullException(nameof(capabilities)); }
    /// <summary>Requests current compilation and reads its explicit last-good status.</summary>
    /// <param name="shader">Current generation shader asset, not retained by the service.</param>
    /// <param name="variant">Exact static keyword selection.</param>
    /// <returns>A detached status snapshot.</returns>
    public EditorShaderCompilationSnapshot Request(ShaderAsset shader, RenderShaderVariant variant)
        => m_provider.RequestShaderCompilation(shader, variant, m_capabilities);
}
