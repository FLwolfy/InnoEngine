using System;

namespace Inno.Editor.Panel.Inspector;

/// <summary>
/// Provides the target-specific header presentation and body drawing behavior for an Inspector target.
/// </summary>
/// <typeparam name="TTarget">The target type inspected by the drawer.</typeparam>
public abstract class InspectorDrawer<TTarget> : IInspectorDrawer
    where TTarget : class
{
    /// <summary>
    /// Gets the icon glyph displayed in the large leading slot of the Inspector target header.
    /// </summary>
    public abstract string icon { get; }

    /// <summary>
    /// Gets the name displayed in the first row of the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    /// <returns>The current target name.</returns>
    protected abstract string GetName(InspectorDrawContext context, TTarget target);

    /// <summary>
    /// Gets an optional callback that changes the name displayed by the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    /// <returns>
    /// A callback that accepts the requested name, or <see langword="null"/> when the name is read-only.
    /// </returns>
    protected virtual Action<string>? GetNameSetter(
        InspectorDrawContext context,
        TTarget target)
        => null;

    /// <summary>
    /// Draws target-specific controls in the second row of the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    /// <remarks>
    /// The callback is clipped to one header row. Implementations should keep all controls on the
    /// current line and must not begin another window, child, table, or popup.
    /// </remarks>
    protected virtual void DrawHeader(InspectorDrawContext context, TTarget target)
    {
    }

    /// <summary>
    /// Draws the target-specific Inspector body below the shared target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    protected abstract void Draw(InspectorDrawContext context, TTarget target);

    string IInspectorDrawer.icon => icon;

    string IInspectorDrawer.GetName(InspectorDrawContext context)
        => GetName(context, GetTarget(context));

    Action<string>? IInspectorDrawer.GetNameSetter(InspectorDrawContext context)
        => GetNameSetter(context, GetTarget(context));

    void IInspectorDrawer.DrawHeader(InspectorDrawContext context)
        => DrawHeader(context, GetTarget(context));

    void IInspectorDrawer.Draw(InspectorDrawContext context)
        => Draw(context, GetTarget(context));

    private static TTarget GetTarget(InspectorDrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.target as TTarget ?? throw new InvalidOperationException(
            $"Inspector drawer for '{typeof(TTarget).FullName}' cannot draw target " +
            $"'{context.target.GetType().FullName}'.");
    }
}

/// <summary>
/// Defines the runtime bridge used to render a discovered Inspector drawer.
/// </summary>
public interface IInspectorDrawer
{
    /// <summary>
    /// Gets the icon glyph displayed in the Inspector target header.
    /// </summary>
    string icon { get; }

    /// <summary>
    /// Gets the target name displayed by the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <returns>The current target name.</returns>
    string GetName(InspectorDrawContext context);

    /// <summary>
    /// Gets the optional target-name mutation callback.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <returns>A name mutation callback, or <see langword="null"/> for a read-only name.</returns>
    Action<string>? GetNameSetter(InspectorDrawContext context);

    /// <summary>
    /// Draws the target-specific second header row.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    void DrawHeader(InspectorDrawContext context);

    /// <summary>
    /// Draws the target-specific Inspector body.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    void Draw(InspectorDrawContext context);
}
