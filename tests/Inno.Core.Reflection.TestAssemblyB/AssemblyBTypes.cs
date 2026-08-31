using System;

namespace Inno.Core.Reflection.TestAssets.B
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AssemblyBMarkerAttribute : Attribute;

    [AssemblyBMarker]
    public sealed class AssemblyBMarkedType;
}
