using Inno.Adapter.Audio;
using Inno.Adapter.Input;
using Inno.Adapter.Platform;
using Inno.Adapter.Rendering;
using Inno.Adapter.Storage;
using Inno.Adapter.Text;
using Inno.Adapter.UI;

namespace Inno.Adapter;

/// <summary>
/// Selects one implementation for every replaceable host backend family.
/// </summary>
public readonly struct AdapterSelection()
{
    /// <summary>
    /// Gets the built-in default adapter selection shipped by the engine distribution.
    /// </summary>
    public static AdapterSelection defaultValue { get; } = new();

    /// <summary>
    /// Gets the selected platform backend.
    /// </summary>
    public PlatformBackend platform { get; init; } = PlatformBackend.Sdl3;

    /// <summary>
    /// Gets the selected input backend.
    /// </summary>
    public InputBackend input { get; init; } = InputBackend.Sdl3;

    /// <summary>
    /// Gets the selected storage backend.
    /// </summary>
    public StorageBackend storage { get; init; } = StorageBackend.FileSystem;

    /// <summary>
    /// Gets the selected rendering backend.
    /// </summary>
    public RenderingBackendId rendering { get; init; } = RenderingBackendId.bgfx;

    /// <summary>
    /// Gets the selected audio backend.
    /// </summary>
    public AudioBackend audio { get; init; } = AudioBackend.MiniAudio;

    /// <summary>
    /// Gets the selected Unicode text backend.
    /// </summary>
    public TextBackend text { get; init; } = TextBackend.FreeTypeHarfBuzz;

    /// <summary>
    /// Gets the selected retained-mode UI backend.
    /// </summary>
    public UiBackendId ui { get; init; } = UiBackendId.rmlUi;
}
