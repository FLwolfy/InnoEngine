using Inno.Editor.Scene.Inspection;

using System;

using Inno.Engine.Scene;

namespace Inno.Editor.Scene.Inspection;

/// <summary>Identifies a system together with its owning scene.</summary>
public sealed class SystemEditorTarget
{
    /// <summary>Creates a system editor target.</summary>
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
