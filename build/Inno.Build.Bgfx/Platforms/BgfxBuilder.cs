using Inno.Build.Global;

namespace Inno.Build.Bgfx.Platforms;

public abstract class BgfxBuilder
{
    public abstract string outputPlatform { get; }
    protected abstract string debugMakeTarget { get; }
    protected abstract string releaseMakeTarget { get; }

    public abstract bool IsSupported();

    public string GetMakeTarget(string config)
    {
        return config == GlobalBuildConstants.DEBUG_CONFIG
            ? debugMakeTarget
            : releaseMakeTarget;
    }

    public abstract void Build(string bgfxDir, string config, string? makeTargetOverride);

    public abstract void BuildTools(string bgfxDir, string config);
}
