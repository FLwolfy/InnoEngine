using System;
using System.Diagnostics.CodeAnalysis;

namespace Inno.Editor.Core.Commands;

/// <summary>Provides contextual state to an editor action.</summary>
public class EditorActionContext
{
    /// <summary>Creates an action context.</summary>
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

    /// <summary>Tries to read the argument as the requested type.</summary>
    public bool TryGetArgument<T>([NotNullWhen(true)] out T? value)
        where T : class
    {
        value = argument as T;
        return value is not null;
    }
}

/// <summary>Provides a strongly typed target to an editor action.</summary>
public sealed class EditorActionContext<TTarget> : EditorActionContext
    where TTarget : class
{
    /// <summary>Creates a typed action context.</summary>
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
