using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions;

/// <summary>
/// Provides contextual state to an editor action.
/// </summary>
public class EditorActionContext
{
    /// <summary>
    /// Creates an action request.
    /// </summary>
    /// <param name="editor">
    /// The shared passive editor context.
    /// </param>
    /// <param name="interactions">
    /// The active interaction entry point.
    /// </param>
    /// <param name="area">
    /// The stable interaction area issuing the request.
    /// </param>
    /// <param name="target">
    /// The optional object the action operates on.
    /// </param>
    /// <param name="argument">
    /// An optional placement-specific argument.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="editor"/> or <paramref name="interactions"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown when <paramref name="area"/> is empty.
    /// </exception>
    internal EditorActionContext(
        EditorContext editor,
        EditorInteractions interactions,
        string area,
        object? target = null,
        object? argument = null)
    {
        this.editor = editor ?? throw new ArgumentNullException(nameof(editor));
        this.interactions = interactions ?? throw new ArgumentNullException(nameof(interactions));
        ArgumentException.ThrowIfNullOrWhiteSpace(area);
        this.area = area;
        this.target = target;
        this.argument = argument;
    }

    /// <summary>
    /// Gets the shared passive editor context.
    /// </summary>
    public EditorContext editor { get; }

    /// <summary>
    /// Gets the active interaction entry point.
    /// </summary>
    public EditorInteractions interactions { get; }

    /// <summary>
    /// Gets the transactional history used to record reversible mutations performed by this action.
    /// </summary>
    public IEditorHistory history => interactions.history;

    /// <summary>
    /// Gets the stable interaction area.
    /// </summary>
    public string area { get; }

    /// <summary>
    /// Gets the contextual action target.
    /// </summary>
    public object? target { get; }

    /// <summary>
    /// Gets the optional action argument.
    /// </summary>
    internal object? argument { get; }
}

/// <summary>
/// Provides a strongly typed target to an editor action implementation.
/// </summary>
/// <typeparam name="TTarget">
/// The target type required by the action.
/// </typeparam>
public sealed class EditorActionContext<TTarget> : EditorActionContext
    where TTarget : class
{
    internal EditorActionContext(EditorActionContext context, TTarget target)
        : base(context.editor, context.interactions, context.area, target, context.argument)
    {
        this.target = target;
    }

    /// <summary>
    /// Gets the strongly typed action target.
    /// </summary>
    public new TTarget target { get; }
}

/// <summary>
/// Provides a strongly typed target and action argument to an editor action.
/// </summary>
/// <typeparam name="TTarget">
/// The target type required by the action.
/// </typeparam>
/// <typeparam name="TArgument">
/// The action argument type required by the action.
/// </typeparam>
public sealed class EditorActionContext<TTarget, TArgument> : EditorActionContext
    where TTarget : class
{
    internal EditorActionContext(EditorActionContext context, TTarget target, TArgument argument)
        : base(context.editor, context.interactions, context.area, target, argument)
    {
        this.target = target;
        this.argument = argument;
    }

    /// <summary>
    /// Gets the strongly typed action target.
    /// </summary>
    public new TTarget target { get; }

    /// <summary>
    /// Gets the strongly typed action argument.
    /// </summary>
    public new TArgument argument { get; }
}

/// <summary>
/// Provides a strongly typed action argument to a targetless editor action.
/// </summary>
/// <typeparam name="TArgument">
/// The action argument type required by the action.
/// </typeparam>
public sealed class EditorActionArgumentContext<TArgument> : EditorActionContext
{
    internal EditorActionArgumentContext(EditorActionContext context, TArgument argument)
        : base(context.editor, context.interactions, context.area, target: null, argument: argument)
    {
        this.argument = argument;
    }

    /// <summary>
    /// Gets the strongly typed action argument.
    /// </summary>
    public new TArgument argument { get; }
}
