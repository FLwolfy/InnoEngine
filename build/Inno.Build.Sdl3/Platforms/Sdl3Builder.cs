using Inno.Build.Global;

namespace Inno.Build.Sdl3.Platforms;

public abstract class Sdl3Builder
{
    public abstract string OutputPlatform { get; }

    public abstract bool IsSupported();

    public abstract void Build(string sdlDir, string config);

    protected static string GetBuildType(string config)
    {
        return config == GlobalBuildConstants.DEBUG_CONFIG ? "Debug" : "Release";
    }
}
