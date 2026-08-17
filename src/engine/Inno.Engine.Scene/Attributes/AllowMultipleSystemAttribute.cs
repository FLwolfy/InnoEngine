using System;

namespace Inno.Engine.Scene;

/// <summary>
/// Allows multiple instances of a concrete <see cref="GameSystem"/> type in one scene.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AllowMultipleSystemAttribute : Attribute;
