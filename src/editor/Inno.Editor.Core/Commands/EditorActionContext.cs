using System;
using System.Diagnostics.CodeAnalysis;

namespace Inno.Editor.Core.Commands;

/// <summary>Provides contextual state to an editor action.</summary>
public class EditorActionContext
{
    /// <summary>
    /// Creates an action context that describes one query or execution request.
    /// </summary>
    /// <param name="editor">The shared editor context.</param>
    /// <param name="surface">The interaction surface that issued the request.</param>
    /// <param name="target">The optional object the action operates on.</param>
    /// <param name="argument">An optional placement-specific argument.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editor"/> or <paramref name="surface"/> is <see langword="null"/>.</exception>
    public EditorActionContext(
        EditorContext editor,
        Type surface,
        object? target = null,
        object? argument = null)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.surface = surface ?? throw new ArgumentNullException(nameof(surface));
        this.target = target;
        this.argument = argument;
    }

    /// <summary>Gets the active editor context.</summary>
    public EditorContext editor { get; }

    /// <summary>Gets the interaction surface that issued the action.</summary>
    public Type surface { get; }

    /// <summary>Gets the contextual action target.</summary>
    public object? target { get; }

    /// <summary>Gets the optional action argument.</summary>
    public object? argument { get; }

    /// <summary>
    /// Tries to read the optional action argument as the requested reference type.
    /// </summary>
    /// <typeparam name="T">The reference type expected by the action implementation.</typeparam>
    /// <param name="value">The typed argument when the method succeeds.</param>
    /// <returns><see langword="true"/> when the argument is assignable to <typeparamref name="T"/>; otherwise, <see langword="false"/>.</returns>
    public bool TryGetArgument<T>([NotNullWhen(true)] out T? value)
        where T : class
    {
        value = argument as T;
        return value is not null;
    }
}

/// <summary>
/// Provides a strongly typed target to an editor action implementation.
/// </summary>
/// <typeparam name="TTarget">The target type required by the action.</typeparam>
public sealed class EditorActionContext<TTarget> : EditorActionContext
    where TTarget : class
{
    /// <summary>
    /// Creates a typed action context for a validated target.
    /// </summary>
    /// <param name="editor">The shared editor context.</param>
    /// <param name="surface">The interaction surface that issued the request.</param>
    /// <param name="target">The validated strongly typed target.</param>
    /// <param name="argument">An optional placement-specific argument.</param>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="editor"/>, <paramref name="surface"/>, or <paramref name="target"/> is <see langword="null"/>.</exception>
    public EditorActionContext(
        EditorContext editor,
        Type surface,
        TTarget target,
        object? argument = null)
        : base(editor, surface, target, argument)
    {
        this.target = target ?? throw new ArgumentNullException(nameof(target));
    }

    /// <summary>Gets the strongly typed action target.</summary>
    public new TTarget target { get; }
}
