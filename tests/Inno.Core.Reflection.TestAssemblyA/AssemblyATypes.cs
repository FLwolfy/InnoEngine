using System;

namespace Inno.Core.Reflection.TestAssets.A
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AssemblyAMarkerAttribute : Attribute;

    [AssemblyAMarker]
    public sealed class AssemblyAMarkedType;
}
