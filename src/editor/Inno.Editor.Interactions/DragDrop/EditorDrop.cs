using System;

using Inno.Editor.Core;

namespace Inno.Editor.Interactions.DragDrop;

/// <summary>Defines one discoverable typed editor drop operation.</summary>
public abstract class EditorDrop
{
    /// <summary>Gets the accepted drag source type.</summary>
    public abstract Type sourceType { get; }

    /// <summary>Gets the accepted drop target type.</summary>
    public abstract Type targetType { get; }

    /// <summary>
    /// Evaluates whether the current managed source may be dropped on the supplied target.
    /// </summary>
    /// <param name="context">The untyped source, target, surface, and requested placement.</param>
    /// <returns>The compatibility state and standard target visual.</returns>
    public abstract EditorDropStatus Query(EditorDropContext context);

    /// <summary>
    /// Executes a delivered drop after a successful compatibility query.
    /// </summary>
    /// <param name="context">The untyped source, target, surface, and requested placement.</param>
    /// <returns>The observable selection and reveal result of the operation.</returns>
    public abstract EditorDropResult Drop(EditorDropContext context);
}

/// <summary>
/// Defines a typed drop operation for one managed source and target pair.
/// </summary>
/// <typeparam name="TSource">The managed drag-source type accepted by the operation.</typeparam>
/// <typeparam name="TTarget">The managed drop-target type accepted by the operation.</typeparam>
public abstract class EditorDrop<TSource, TTarget> : EditorDrop
    where TSource : class
    where TTarget : class
{
    /// <inheritdoc />
    public sealed override Type sourceType => typeof(TSource);

    /// <inheritdoc />
    public sealed override Type targetType => typeof(TTarget);

    /// <inheritdoc />
    /// <param name="context">The untyped drop context supplied by the runtime router.</param>
    /// <returns>The typed handler result, or a rejected state when the source or target is incompatible.</returns>
    public sealed override EditorDropStatus Query(EditorDropContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.data.source is TSource source && context.target is TTarget target
            ? Query(new EditorDropContext<TSource, TTarget>(context, source, target))
            : EditorDropStatus.rejected;
    }

    /// <inheritdoc />
    /// <param name="context">The untyped drop context supplied by the runtime router.</param>
    /// <returns>The typed handler result, or a rejected result when the source or target is incompatible.</returns>
    public sealed override EditorDropResult Drop(EditorDropContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.data.source is not TSource source || context.target is not TTarget target)
            return EditorDropResult.rejected;
        return Drop(new EditorDropContext<TSource, TTarget>(context, source, target));
    }

    /// <summary>
    /// Evaluates whether the strongly typed source may be dropped on the strongly typed target.
    /// </summary>
    /// <param name="context">The typed source, target, surface, and requested placement.</param>
    /// <returns>The compatibility state and standard target visual.</returns>
    protected abstract EditorDropStatus Query(EditorDropContext<TSource, TTarget> context);

    /// <summary>
    /// Executes a delivered drop for the strongly typed source and target.
    /// </summary>
    /// <param name="context">The typed source, target, surface, and requested placement.</param>
    /// <returns>The observable selection and reveal result of the operation.</returns>
    protected abstract EditorDropResult Drop(EditorDropContext<TSource, TTarget> context);
}

/// <summary>
/// Provides strongly typed source and target values to a drop operation.
/// </summary>
/// <typeparam name="TSource">The managed drag-source type.</typeparam>
/// <typeparam name="TTarget">The managed drop-target type.</typeparam>
public sealed class EditorDropContext<TSource, TTarget>
    where TSource : class
    where TTarget : class
{
    internal EditorDropContext(EditorDropContext context, TSource source, TTarget target)
    {
        untyped = context;
        this.source = source;
        this.target = target;
    }

    /// <summary>Gets the untyped drop context.</summary>
    public EditorDropContext untyped { get; }

    /// <summary>Gets the active editor context.</summary>
    public EditorContext editor => untyped.editor;

    /// <summary>Gets the active interaction entry point.</summary>
    public EditorInteractions interactions => untyped.interactions;

    /// <summary>Gets the interaction area.</summary>
    public string area => untyped.area;

    /// <summary>Gets the typed drag source.</summary>
    public TSource source { get; }

    /// <summary>Gets the typed drop target.</summary>
    public TTarget target { get; }

    /// <summary>Gets the requested placement.</summary>
    public EditorDropPlacement placement => untyped.placement;
}
