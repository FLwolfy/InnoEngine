using System;

namespace Inno.Scene;

/// <summary>
/// Allows multiple instances of a concrete component type on one game object.
/// </summary>
[AttributeUsage(AttributeTargets.Class, Inherited = true)]
public sealed class AllowMultipleComponentAttribute : Attribute;
