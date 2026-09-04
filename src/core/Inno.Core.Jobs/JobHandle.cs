namespace Inno.Core.Jobs;

/// <summary>
/// Opaque handle representing a scheduled job.
/// </summary>
public readonly struct JobHandle
{
    internal JobHandle(int index, int version)
    {
        this.index = index;
        this.version = version;
    }

    internal int index { get; }
    internal int version { get; }

    /// <summary>
    /// Gets whether this handle contains a valid identifier.
    /// </summary>
    public bool isValid => index >= 0 && version > 0;
}
