using System;

namespace Inno.Extensibility.Types.TestAssets.B
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AssemblyBMarkerAttribute : Attribute;

    [AssemblyBMarker]
    public sealed class AssemblyBMarkedType;
}
