using System;

namespace Inno.Extensibility.Types.TestAssets.A
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AssemblyAMarkerAttribute : Attribute;

    [AssemblyAMarker]
    public sealed class AssemblyAMarkedType;
}
