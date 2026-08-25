namespace Inno.Editor.Interactions;

/// <summary>Represents a strongly identified editor command without an argument.</summary>
public readonly record struct EditorCommand
{
    /// <summary>Creates a command for one stable action.</summary>
    /// <param name="id">The stable action identifier.</param>
    public EditorCommand(EditorActionId id) => this.id = id;

    /// <summary>Gets the stable action identifier.</summary>
    public EditorActionId id { get; }
}

/// <summary>Represents a strongly identified editor command carrying one typed argument.</summary>
/// <typeparam name="TArgument">The argument type validated before action dispatch.</typeparam>
public readonly record struct EditorCommand<TArgument>
{
    /// <summary>Creates a typed command for one stable action.</summary>
    /// <param name="id">The stable action identifier.</param>
    public EditorCommand(EditorActionId id) => this.id = id;

    /// <summary>Gets the stable action identifier.</summary>
    public EditorActionId id { get; }
}
