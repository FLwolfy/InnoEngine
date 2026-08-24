using System;

namespace Inno.Editor.Inspection;

/// <summary>
/// Provides the target-specific header presentation and body drawing behavior for an inspected target.
/// </summary>
/// <typeparam name="TTarget">The target type inspected by the drawer.</typeparam>
public abstract class InspectionDrawer<TTarget> : IInspectionDrawer
    where TTarget : class
{
    /// <summary>
    /// Gets the icon glyph displayed in the large leading slot of the Inspector target header.
    /// </summary>
    public abstract string icon { get; }

    /// <summary>
    /// Binds the name displayed in the first row of the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    /// <returns>
    /// The current target name and a callback that accepts a requested replacement name, or
    /// <see langword="null"/> for the callback when the name is read-only.
    /// </returns>
    protected abstract (string name, Action<string>? setter) BindName(
        InspectionDrawContext context,
        TTarget target);

    /// <summary>
    /// Resolves the icon glyph displayed for the current target.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    /// <returns>
    /// The target-specific icon glyph. The default implementation returns <see cref="icon"/>.
    /// </returns>
    protected virtual string GetIcon(InspectionDrawContext context, TTarget target)
        => icon;

    /// <summary>
    /// Draws target-specific controls in the second row of the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    /// <remarks>
    /// The callback is clipped to one header row. Implementations should keep all controls on the
    /// current line and must not begin another window, child, table, or popup.
    /// </remarks>
    protected virtual void DrawHeader(InspectionDrawContext context, TTarget target)
    {
    }

    /// <summary>
    /// Draws the target-specific Inspector body below the shared target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <param name="target">The strongly typed target being inspected.</param>
    protected abstract void Draw(InspectionDrawContext context, TTarget target);

    string IInspectionDrawer.GetIcon(InspectionDrawContext context)
        => GetIcon(context, GetTarget(context));

    (string name, Action<string>? setter) IInspectionDrawer.BindName(
        InspectionDrawContext context)
        => BindName(context, GetTarget(context));

    void IInspectionDrawer.DrawHeader(InspectionDrawContext context)
        => DrawHeader(context, GetTarget(context));

    void IInspectionDrawer.Draw(InspectionDrawContext context)
        => Draw(context, GetTarget(context));

    private static TTarget GetTarget(InspectionDrawContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.target as TTarget ?? throw new InvalidOperationException(
            $"Inspector drawer for '{typeof(TTarget).FullName}' cannot draw target " +
            $"'{context.target.GetType().FullName}'.");
    }
}

/// <summary>
/// Defines the runtime bridge used to render a discovered inspection drawer.
/// </summary>
public interface IInspectionDrawer
{
    /// <summary>
    /// Gets the icon glyph displayed in the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <returns>The icon glyph for the current target.</returns>
    string GetIcon(InspectionDrawContext context);

    /// <summary>
    /// Binds the target name displayed by the Inspector target header.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    /// <returns>
    /// The current target name and a callback that accepts a requested replacement name, or
    /// <see langword="null"/> for the callback when the name is read-only.
    /// </returns>
    (string name, Action<string>? setter) BindName(InspectionDrawContext context);

    /// <summary>
    /// Draws the target-specific second header row.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    void DrawHeader(InspectionDrawContext context);

    /// <summary>
    /// Draws the target-specific Inspector body.
    /// </summary>
    /// <param name="context">The active Inspector drawing context.</param>
    void Draw(InspectionDrawContext context);
}
