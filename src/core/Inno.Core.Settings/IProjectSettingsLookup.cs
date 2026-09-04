using Inno.Core.Serialization;

namespace Inno.Core.Settings;

/// <summary>
/// Defines the read-only effective settings boundary shared by Editor and Player hosts.
/// </summary>
public interface IProjectSettingsLookup
{
    /// <summary>
    /// Gets the monotonic revision of the active effective settings snapshot.
    /// </summary>
    long revision { get; }

    /// <summary>
    /// Gets an isolated effective setting from the active extension generation.
    /// </summary>
    /// <typeparam name="TSetting">
    /// The required setting contract.
    /// </typeparam>
    /// <param name="id">
    /// The stable setting protocol identity.
    /// </param>
    /// <returns>
    /// An independently owned effective setting snapshot.
    /// </returns>
    TSetting Get<TSetting>(ProjectSettingId id)
        where TSetting : class, ISerializable;

    /// <summary>
    /// Tries to get an isolated effective setting from the active extension generation.
    /// </summary>
    /// <typeparam name="TSetting">
    /// The required setting contract.
    /// </typeparam>
    /// <param name="id">
    /// The stable setting protocol identity.
    /// </param>
    /// <param name="setting">
    /// Receives an independently owned effective snapshot when available.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when a compatible setting exists; otherwise,
    /// <see langword="false"/>.
    /// </returns>
    bool TryGet<TSetting>(ProjectSettingId id, out TSetting? setting)
        where TSetting : class, ISerializable;
}
