using Inno.Build.Global;

namespace Inno.Build.CImguizmo.Platforms;

public abstract class CImguizmoBuilder
{
    public abstract string outputPlatform { get; }
    
    public abstract bool IsSupported();
    
    public abstract void Build(string cimguizmoDir, string cimguiDir, string cimguiBuildDir, string cimguiOutputDir, string config);

    protected static string GetBuildType(string config)
    {
        return config == GlobalBuildConstants.DEBUG_CONFIG ? "Debug" : "Release";
    }
}
