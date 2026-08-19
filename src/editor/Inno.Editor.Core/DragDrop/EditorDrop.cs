using System;

namespace Inno.Editor.Core.DragDrop;

/// <summary>Defines one discoverable typed editor drop operation.</summary>
public abstract class EditorDrop
{
    /// <summary>Gets the accepted drag source type.</summary>
    public abstract Type sourceType { get; }

    /// <summary>Gets the accepted drop target type.</summary>
    public abstract Type targetType { get; }

    /// <summary>Evaluates a potential drop.</summary>
    public abstract EditorDropStatus Query(EditorDropContext context);

    /// <summary>Executes a delivered drop.</summary>
    public abstract EditorDropResult Drop(EditorDropContext context);
}

/// <summary>Defines a typed drop operation.</summary>
public abstract class EditorDrop<TSource, TTarget> : EditorDrop
    where TSource : class
    where TTarget : class
{
    /// <inheritdoc />
    public sealed override Type sourceType => typeof(TSource);

    /// <inheritdoc />
    public sealed override Type targetType => typeof(TTarget);

    /// <inheritdoc />
    public sealed override EditorDropStatus Query(EditorDropContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.data.source is TSource source && context.target is TTarget target
            ? Query(new EditorDropContext<TSource, TTarget>(context, source, target))
            : EditorDropStatus.rejected;
    }

    /// <inheritdoc />
    public sealed override EditorDropResult Drop(EditorDropContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (context.data.source is not TSource source || context.target is not TTarget target)
            return EditorDropResult.rejected;
        return Drop(new EditorDropContext<TSource, TTarget>(context, source, target));
    }

    /// <summary>Evaluates a typed drop.</summary>
    protected abstract EditorDropStatus Query(EditorDropContext<TSource, TTarget> context);

    /// <summary>Executes a typed drop.</summary>
    protected abstract EditorDropResult Drop(EditorDropContext<TSource, TTarget> context);
}

/// <summary>Provides strongly typed source and target values to a drop operation.</summary>
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
    public EditorContext editor => untyped.editorContext;

    /// <summary>Gets the interaction surface.</summary>
    public Type surface => untyped.surface;

    /// <summary>Gets the typed drag source.</summary>
    public TSource source { get; }

    /// <summary>Gets the typed drop target.</summary>
    public TTarget target { get; }

    /// <summary>Gets the requested placement.</summary>
    public EditorDropPlacement placement => untyped.placement;
}
