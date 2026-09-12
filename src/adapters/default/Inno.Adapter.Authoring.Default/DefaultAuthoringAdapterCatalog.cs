using System;
using System.Collections.Generic;

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
    private readonly DefaultAdapterCatalog m_runtime;
    private readonly RenderingAuthoringBackendCatalog m_renderingAuthoring;

    /// <summary>Creates paired runtime and authoring registrations before any native device is initialized.</summary>
    /// <param name="renderingProviders">Complete runtime rendering registrations, or null for bundled BGFX.</param>
    /// <param name="authoringProviders">Complete matching authoring registrations, or null for bundled BGFX.</param>
    /// <exception cref="ArgumentException">The runtime and authoring backend registrations do not match.</exception>
    public DefaultAuthoringAdapterCatalog(
        IEnumerable<RenderingBackendProvider>? renderingProviders = null,
        IEnumerable<RenderingAuthoringBackendProvider>? authoringProviders = null)
    {
        m_runtime = new DefaultAdapterCatalog(renderingProviders);
        m_renderingAuthoring = new RenderingAuthoringBackendCatalog(
            m_runtime.rendering,
            authoringProviders ?? [new BgfxAuthoringProvider()]);
    }

    IReadOnlyList<RenderingBackendId> IRenderingAuthoringBackendFactory.supportedBackends
        => m_renderingAuthoring.supportedBackends;

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
        RenderingBackendId backend)
        => m_renderingAuthoring.CreateShaderCompilerToolchain(backend);

    ITextureTargetCompiler IRenderingAuthoringBackendFactory.CreateTextureTargetCompiler(
        RenderingBackendId backend)
        => m_renderingAuthoring.CreateTextureTargetCompiler(backend);

    private sealed class BgfxAuthoringProvider : RenderingAuthoringBackendProvider
    {
        public override RenderingBackendId id => RenderingBackendId.bgfx;
        public override IShaderCompilerToolchain CreateShaderCompilerToolchain() => new BgfxShadercToolchain();
        public override ITextureTargetCompiler CreateTextureTargetCompiler() => new BgfxTextureTargetCompiler();
    }

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
