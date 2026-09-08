using System;

using Inno.Core.Serialization;

namespace Inno.Core.Settings;

/// <summary>
/// Provides script-facing project settings queries through the current runtime execution context.
/// </summary>
/// <remarks>
/// This façade owns no settings state. Engine and Editor infrastructure should depend on an explicit
/// <see cref="IProjectSettingsLookup"/> instance.
/// </remarks>
public static class Settings
{
    /// <summary>
    /// Gets the current project namespace used to qualify project-authored logical names.
    /// </summary>
    public static ProjectId projectId
        => Get<ProjectIdentitySettings>(ProjectIdentitySettings.settingId).id;

    /// <summary>
    /// Creates one complete project identity from a display or local name.
    /// </summary>
    /// <param name="name">
    /// The project-local name.
    /// </param>
    /// <returns>
    /// The canonical <c>projectId.name</c> identity.
    /// </returns>
    public static ProjectScopedId QualifyId(string name)
        => projectId.Qualify(ProjectLocalId.FromName(name));

    /// <summary>
    /// Gets the revision of the settings snapshot active in the current execution context.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no project settings lookup is active for the caller.
    /// </exception>
    public static long revision => ProjectSettingsExecutionContext.current.revision;

    /// <summary>
    /// Gets an isolated effective setting from the current extension generation.
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when no project settings lookup is active or the setting is unavailable.
    /// </exception>
    public static TSetting Get<TSetting>(ProjectSettingId id)
        where TSetting : class, ISerializable
        => ProjectSettingsExecutionContext.current.Get<TSetting>(id);

    /// <summary>
    /// Tries to get an isolated effective setting from the current extension generation.
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
    /// <exception cref="InvalidOperationException">
    /// Thrown when no project settings lookup is active for the caller.
    /// </exception>
    public static bool TryGet<TSetting>(ProjectSettingId id, out TSetting? setting)
        where TSetting : class, ISerializable
        => ProjectSettingsExecutionContext.current.TryGet(id, out setting);
}
