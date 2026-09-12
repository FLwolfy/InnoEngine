using System;

namespace Inno.Rendering.Shaders;

/// <summary>Marks a source language frontend for discovery in the current authoring type generation.</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class ShaderSourceFrontendExtensionAttribute : Attribute;
