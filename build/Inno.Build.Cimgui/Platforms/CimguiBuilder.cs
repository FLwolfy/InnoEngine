using Inno.Build.Global;

namespace Inno.Build.Cimgui.Platforms;

public abstract class CimguiBuilder
{
    public abstract string outputPlatform { get; }
    
    public abstract bool IsSupported();
    
    public abstract void Build(string cimguiDir, string config);

    protected static string GetBuildType(string config)
    {
        return config == GlobalBuildConstants.DEBUG_CONFIG ? "Debug" : "Release";
    }
}
