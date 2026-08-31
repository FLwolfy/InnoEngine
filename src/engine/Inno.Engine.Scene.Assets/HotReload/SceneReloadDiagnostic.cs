using System;

namespace Inno.Engine.Scene.Assets;

/// <summary>
/// Describes a non-fatal issue encountered while migrating scene state to a new assembly generation.
/// </summary>
public sealed class SceneReloadDiagnostic
{
    internal SceneReloadDiagnostic(
        string code,
        SceneReloadDiagnosticSeverity severity,
        string message,
        Guid scenePersistentId,
        Guid objectPersistentId,
        string propertyName,
        string previousPropertyType,
        string currentPropertyType)
    {
        this.code = code;
        this.severity = severity;
        this.message = message;
        this.scenePersistentId = scenePersistentId;
        this.objectPersistentId = objectPersistentId;
        this.propertyName = propertyName;
        this.previousPropertyType = previousPropertyType;
        this.currentPropertyType = currentPropertyType;
    }

    /// <summary>Gets the stable diagnostic code.</summary>
    public string code { get; }

    /// <summary>Gets the diagnostic severity.</summary>
    public SceneReloadDiagnosticSeverity severity { get; }

    /// <summary>Gets the human-readable diagnostic message.</summary>
    public string message { get; }

    /// <summary>Gets the persistent identity of the affected scene.</summary>
    public Guid scenePersistentId { get; }

    /// <summary>Gets the persistent identity of the affected component or system.</summary>
    public Guid objectPersistentId { get; }

    /// <summary>Gets the incompatible serialized property name.</summary>
    public string propertyName { get; }

    /// <summary>Gets the previous declared property type name.</summary>
    public string previousPropertyType { get; }

    /// <summary>Gets the current declared property type name.</summary>
    public string currentPropertyType { get; }
}
