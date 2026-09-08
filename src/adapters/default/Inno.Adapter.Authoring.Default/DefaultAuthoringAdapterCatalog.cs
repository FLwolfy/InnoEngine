using System;

using Inno.Adapter.Audio;
using Inno.Adapter.Default;
using Inno.Adapter.Input;
using Inno.Adapter.Platform;
using Inno.Adapter.Presentation;
using Inno.Adapter.Rendering;
using Inno.Adapter.Storage;
using Inno.Build.Toolchains.Bgfx.Tools;
using Inno.Rendering.Assets;

namespace Inno.Adapter.Authoring.Default;

/// <summary>
/// Adds the standard rendering toolchain and ImGui presentation to the built-in runtime adapter catalog.
/// </summary>
public sealed class DefaultAuthoringAdapterCatalog :
    IAuthoringAdapterCatalog,
    IRenderingAuthoringBackendFactory,
    IPresentationBackendFactory
{
    private readonly DefaultAdapterCatalog m_runtime = new();

    /// <summary>
    /// Gets the built-in platform backend factory.
    /// </summary>
    public IPlatformBackendFactory platform => m_runtime.platform;

    /// <summary>
    /// Gets the built-in input backend factory.
    /// </summary>
    public IInputBackendFactory input => m_runtime.input;

    /// <summary>
    /// Gets the built-in application-storage backend factory.
    /// </summary>
    public IStorageBackendFactory storage => m_runtime.storage;

    /// <summary>
    /// Gets the built-in runtime rendering backend factory.
    /// </summary>
    public IRenderingBackendFactory rendering => m_runtime.rendering;

    /// <summary>
    /// Gets the built-in audio backend factory.
    /// </summary>
    public IAudioBackendFactory audio => m_runtime.audio;

    /// <summary>
    /// Gets the built-in rendering authoring toolchain factory.
    /// </summary>
    public IRenderingAuthoringBackendFactory renderingAuthoring => this;

    /// <summary>
    /// Gets the built-in graphical host-presentation factory.
    /// </summary>
    public IPresentationBackendFactory presentation => this;

    IShaderCompilerToolchain IRenderingAuthoringBackendFactory.CreateShaderCompilerToolchain(
        RenderingBackend backend)
        => backend switch
        {
            RenderingBackend.Bgfx => new BgfxShadercToolchain(),
            _ => throw Unsupported(nameof(backend), backend)
        };

    ITextureTargetCompiler IRenderingAuthoringBackendFactory.CreateTextureTargetCompiler(
        RenderingBackend backend)
        => backend switch
        {
            RenderingBackend.Bgfx => new BgfxTextureTargetCompiler(),
            _ => throw Unsupported(nameof(backend), backend)
        };

    IPresentationContext IPresentationBackendFactory.CreateContext(
        PresentationBackend backend,
        PresentationBackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return backend switch
        {
            PresentationBackend.ImGui => new ImGuiPresentationContext(options),
            _ => throw Unsupported(nameof(backend), backend)
        };
    }

    private static NotSupportedException Unsupported<TBackend>(string parameterName, TBackend backend)
        where TBackend : struct, Enum
        => new($"The {parameterName} selection '{backend}' is not available in the default authoring adapter catalog.");
}
