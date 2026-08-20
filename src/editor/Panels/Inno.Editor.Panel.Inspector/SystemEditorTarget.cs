using Inno.Editor.Panel.Inspector;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Panel.Inspector;

/// <summary>Identifies a system together with its owning scene.</summary>
public sealed class SystemEditorTarget
{
    /// <summary>
    /// Creates an inspector action target that keeps a system paired with its owning scene.
    /// </summary>
    /// <param name="scene">The loaded scene that owns the system.</param>
    /// <param name="system">The system operated on by the inspector action.</param>
    /// <exception cref="ArgumentNullException">Thrown when either argument is <see langword="null"/>.</exception>
    public SystemEditorTarget(GameScene scene, GameSystem system)
    {
        this.scene = scene ?? throw new ArgumentNullException(nameof(scene));
        this.system = system ?? throw new ArgumentNullException(nameof(system));
    }

    /// <summary>Gets the owning scene.</summary>
    public GameScene scene { get; }

    /// <summary>Gets the system.</summary>
    public GameSystem system { get; }
}
