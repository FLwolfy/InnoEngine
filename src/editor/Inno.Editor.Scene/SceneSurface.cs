namespace Inno.Editor.Scene;

/// <summary>Provides strongly typed interaction surfaces owned by the Scene editor feature.</summary>
public static class SceneSurface
{
    /// <summary>Identifies a Scene row in Hierarchy.</summary>
    public sealed class HierarchyScene;

    /// <summary>Identifies a GameObject row in Hierarchy.</summary>
    public sealed class HierarchyObject;

    /// <summary>Identifies empty Hierarchy space.</summary>
    public sealed class HierarchyBlank;

    /// <summary>Identifies a Component card menu.</summary>
    public sealed class Component;

    /// <summary>Identifies a GameSystem card menu.</summary>
    public sealed class System;

    /// <summary>Identifies the searchable Add Component menu.</summary>
    public sealed class AddComponent;

    /// <summary>Identifies the searchable Add System menu.</summary>
    public sealed class AddSystem;

    /// <summary>Identifies an EngineObject-reference property target.</summary>
    public sealed class EngineObjectReference;
}
