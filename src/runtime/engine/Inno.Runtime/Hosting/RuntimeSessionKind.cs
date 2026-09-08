namespace Inno.Runtime;

/// <summary>
/// Identifies the ownership and lifecycle semantics of an isolated runtime session.
/// </summary>
public enum RuntimeSessionKind
{
    /// <summary>
    /// An authoring session whose scene world remains editable.
    /// </summary>
    Edit,

    /// <summary>
    /// A disposable Editor play-test session created from an immutable start snapshot.
    /// </summary>
    Play,

    /// <summary>
    /// A deployed standalone game session backed only by runtime artifacts.
    /// </summary>
    Player
}
