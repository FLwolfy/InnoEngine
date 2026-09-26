using System;
using System.Collections.Generic;

using Inno.Adapter.Audio;
using Inno.Adapter.Audio.MiniAudio;
using Inno.Adapter.Input;
using Inno.Adapter.Input.Sdl3;
using Inno.Adapter.Platform;
using Inno.Adapter.Platform.Sdl3;
using Inno.Adapter.Rendering;
using Inno.Adapter.Rendering.Bgfx;
using Inno.Adapter.Storage;
using Inno.Adapter.Storage.FileSystem;
using Inno.Adapter.Text;
using Inno.Adapter.Text.FreeTypeHarfBuzz;
using Inno.Adapter.UI;
using Inno.Adapter.UI.RmlUi;
using Inno.Audio;
using Inno.Platform;
using Inno.Rendering;
using Inno.Storage;
using Inno.Text;
using Inno.UI;

namespace Inno.Adapter.Default;

/// <summary>
/// Supplies the coherent built-in runtime backend set shipped by the standard engine distribution.
/// </summary>
public sealed class DefaultAdapterCatalog :
    IAdapterCatalog,
    IPlatformBackendFactory,
    IInputBackendFactory,
    IStorageBackendFactory,
    IRenderingBackendFactory,
    IAudioBackendFactory,
    ITextBackendFactory,
    IUiBackendFactory
{
    private readonly RenderingBackendCatalog m_rendering;
    private readonly UiBackendCatalog m_ui;

    /// <summary>
    /// Creates the standard adapters with optional complete provider sets.
    /// </summary>
    /// <param name="renderingProviders">
    /// Replacement rendering providers, or null to use bundled BGFX.
    /// </param>
    /// <param name="uiProviders">
    /// Replacement UI providers, or null to use bundled RmlUi.
    /// </param>
    public DefaultAdapterCatalog(
        IEnumerable<RenderingBackendProvider>? renderingProviders = null,
        IEnumerable<UiBackendProvider>? uiProviders = null)
    {
        m_rendering = new RenderingBackendCatalog(renderingProviders ?? [new BgfxRenderingProvider()]);
        m_ui = new UiBackendCatalog(uiProviders ?? [new RmlUiProvider()]);
    }

    IReadOnlyList<RenderingBackendId> IRenderingBackendFactory.supportedBackends => m_rendering.supportedBackends;
    IReadOnlyList<UiBackendId> IUiBackendFactory.supportedBackends => m_ui.supportedBackends;

    /// <summary>
    /// Gets the built-in platform backend factory.
    /// </summary>
    public IPlatformBackendFactory platform => this;

    /// <summary>
    /// Gets the built-in input backend factory.
    /// </summary>
    public IInputBackendFactory input => this;

    /// <summary>
    /// Gets the built-in application-storage backend factory.
    /// </summary>
    public IStorageBackendFactory storage => this;

    /// <summary>
    /// Gets the built-in runtime rendering backend factory.
    /// </summary>
    public IRenderingBackendFactory rendering => this;

    /// <summary>
    /// Gets the built-in audio backend factory.
    /// </summary>
    public IAudioBackendFactory audio => this;

    /// <summary>
    /// Gets the built-in Unicode text backend factory.
    /// </summary>
    public ITextBackendFactory text => this;

    /// <summary>
    /// Gets the built-in retained-mode UI backend factory.
    /// </summary>
    public IUiBackendFactory ui => this;

    IPlatformApplication IPlatformBackendFactory.CreateApplication(PlatformBackend backend)
        => backend switch
        {
            PlatformBackend.Sdl3 => new Sdl3PlatformApplication(),
            _ => throw Unsupported(nameof(backend), backend)
        };

    IInputEventSource IInputBackendFactory.CreateEventSource(InputBackend backend, IPlatformWindow window)
    {
        ArgumentNullException.ThrowIfNull(window);
        return backend switch
        {
            InputBackend.Sdl3 => new Sdl3InputSource(window.windowId),
            _ => throw Unsupported(nameof(backend), backend)
        };
    }

    IApplicationStorage IStorageBackendFactory.CreateStorage(StorageBackend backend, string rootDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootDirectory);
        return backend switch
        {
            StorageBackend.FileSystem => new FileSystemApplicationStorage(rootDirectory),
            _ => throw Unsupported(nameof(backend), backend)
        };
    }

    IRenderDevice IRenderingBackendFactory.CreateDevice(
        RenderingBackendId backend,
        RenderingBackendOptions options)
        => m_rendering.CreateDevice(backend, options);

    private sealed class BgfxRenderingProvider : RenderingBackendProvider
    {
        /// <summary>
        /// Gets the stable identity used to reference this value across subsystem boundaries.
        /// </summary>
public override RenderingBackendId id => RenderingBackendId.bgfx;

        /// <summary>
        /// Creates and validates a caller-owned device value.
        /// </summary>
        /// <param name="options">
        /// The validated configuration that controls this operation.
        /// </param>
        /// <returns>
        /// The validated irender device that represents the completed operation.
        /// </returns>
public override IRenderDevice CreateDevice(RenderingBackendOptions options)
            => new BgfxDevice(new BgfxDeviceOptions
            {
                window = options.window,
                preferredBackend = options.preferredGraphicsApi,
                verticalSync = options.verticalSync,
                sRgbBackbuffer = options.sRgbBackbuffer,
                forceSingleThreaded = options.forceSingleThreaded
            });
    }

    IAudioDevice IAudioBackendFactory.CreateDevice(AudioBackend backend, AudioBackendOptions options)
        => backend switch
        {
            AudioBackend.MiniAudio => new MiniAudioDevice(new MiniAudioDeviceOptions
            {
                noDevice = options.noDevice
            }),
            _ => throw Unsupported(nameof(backend), backend)
        };

    ITextBackend ITextBackendFactory.CreateBackend(TextBackend backend)
        => backend switch
        {
            TextBackend.FreeTypeHarfBuzz => new FreeTypeHarfBuzzTextBackend(),
            _ => throw Unsupported(nameof(backend), backend)
        };

    IUiBackend IUiBackendFactory.CreateBackend(UiBackendId backend) => m_ui.CreateBackend(backend);

    private sealed class RmlUiProvider : UiBackendProvider
    {
        /// <summary>
        /// Gets the stable identity used to reference this value across subsystem boundaries.
        /// </summary>
public override UiBackendId id => UiBackendId.rmlUi;

        /// <summary>
        /// Creates and validates a caller-owned backend value.
        /// </summary>
        /// <returns>
        /// The validated iui backend that represents the completed operation.
        /// </returns>
public override IUiBackend CreateBackend() => new RmlUiBackend();
    }

    private static NotSupportedException Unsupported<TBackend>(string parameterName, TBackend backend)
        where TBackend : struct, Enum
        => new($"The {parameterName} selection '{backend}' is not available in the default adapter catalog.");
}
