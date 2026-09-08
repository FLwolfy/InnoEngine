namespace Inno.Runtime.Contracts;

/// <summary>
/// Identifies the exclusive owner that creates and retires a stable engine subsystem.
/// </summary>
public enum RuntimeSubsystemLifetime
{
    /// <summary>
    /// The subsystem is shared by sessions and retires with the application host.
    /// </summary>
    Host,
    /// <summary>
    /// The subsystem belongs to one isolated Edit, Play or Player session.
    /// </summary>
    Session
}
