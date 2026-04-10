using System;
using Inno.Core.Reflection;

namespace Inno.Core.Reflection.TestAssets.A
{
    [AttributeUsage(AttributeTargets.Class)]
    public sealed class AssemblyAMarkerAttribute : Attribute;

    [AssemblyAMarker]
    public sealed class AssemblyAMarkedType;

    public static class AssemblyAHooks
    {
        public static int initializeCount { get; private set; }
        public static int rebuildCount { get; private set; }
        public static int mismatchedInitializeCount { get; private set; }
        public static int mismatchedRebuildCount { get; private set; }

        public static void Reset()
        {
            initializeCount = 0;
            rebuildCount = 0;
            mismatchedInitializeCount = 0;
            mismatchedRebuildCount = 0;
        }

        [TypeCacheInitialize("Inno.Core.Reflection.TestAssemblyA")]
        private static void OnInitialize()
        {
            initializeCount++;
        }

        [TypeCacheRebuild("Inno.Core.Reflection.TestAssemblyA")]
        private static void OnRebuild()
        {
            rebuildCount++;
        }

        [TypeCacheInitialize("Inno.Core.Reflection.TestAssemblyB")]
        private static void OnInitializeMismatched()
        {
            mismatchedInitializeCount++;
        }

        [TypeCacheRebuild("Inno.Core.Reflection.TestAssemblyB")]
        private static void OnRebuildMismatched()
        {
            mismatchedRebuildCount++;
        }
    }
}
