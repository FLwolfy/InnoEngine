namespace Inno.Audio;

/// <summary>
/// Provides protected opaque-handle encoding and validation helpers for audio backends.
/// </summary>
public abstract class AudioDevice
{
    /// <summary>
    /// Stores one decoded device-owned identity.
    /// </summary>
    /// <param name="value">
    /// Non-zero backend resource identity.
    /// </param>
    /// <param name="generation">
    /// Device generation that owns the resource.
    /// </param>
    protected readonly record struct DeviceHandleIdentity(ulong value, uint generation);

    /// <summary>
    /// Encodes a clip identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// Non-zero backend clip identity.
    /// </param>
    /// <param name="generation">
    /// Owning device generation.
    /// </param>
    /// <returns>
    /// An opaque clip handle.
    /// </returns>
    protected static AudioClipHandle CreateClipHandle(ulong value, uint generation) => new(value, generation);

    /// <summary>
    /// Encodes a voice identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// Non-zero backend voice identity.
    /// </param>
    /// <param name="generation">
    /// Owning device generation.
    /// </param>
    /// <returns>
    /// An opaque voice handle.
    /// </returns>
    protected static AudioVoiceHandle CreateVoiceHandle(ulong value, uint generation) => new(value, generation);

    /// <summary>
    /// Encodes a bus identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// Non-zero backend bus identity.
    /// </param>
    /// <param name="generation">
    /// Owning device generation.
    /// </param>
    /// <returns>
    /// An opaque bus handle.
    /// </returns>
    protected static AudioBusHandle CreateBusHandle(ulong value, uint generation) => new(value, generation);

    /// <summary>
    /// Encodes a listener identity into a backend-neutral handle.
    /// </summary>
    /// <param name="value">
    /// Non-zero backend listener identity.
    /// </param>
    /// <param name="generation">
    /// Owning device generation.
    /// </param>
    /// <returns>
    /// An opaque listener handle.
    /// </returns>
    protected static AudioListenerHandle CreateListenerHandle(ulong value, uint generation) => new(value, generation);

    /// <summary>
    /// Decodes a clip handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// Backend-neutral clip handle.
    /// </param>
    /// <returns>
    /// Backend identity and owning generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(AudioClipHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a voice handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// Backend-neutral voice handle.
    /// </param>
    /// <returns>
    /// Backend identity and owning generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(AudioVoiceHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a bus handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// Backend-neutral bus handle.
    /// </param>
    /// <returns>
    /// Backend identity and owning generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(AudioBusHandle handle)
        => new(handle.value, handle.deviceGeneration);

    /// <summary>
    /// Decodes a listener handle for backend lookup and generation validation.
    /// </summary>
    /// <param name="handle">
    /// Backend-neutral listener handle.
    /// </param>
    /// <returns>
    /// Backend identity and owning generation.
    /// </returns>
    protected static DeviceHandleIdentity GetHandleIdentity(AudioListenerHandle handle)
        => new(handle.value, handle.deviceGeneration);
}
