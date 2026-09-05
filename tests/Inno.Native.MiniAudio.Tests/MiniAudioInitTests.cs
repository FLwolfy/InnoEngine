using Xunit;
using Xunit.Abstractions;

namespace Inno.Native.MiniAudio.Tests;

public sealed class MiniAudioInitTests
{
    private readonly ITestOutputHelper m_output;

    public MiniAudioInitTests(ITestOutputHelper output)
    {
        m_output = output;
    }

    [Fact]
    public void VersionLoadsTheCompleteNativeFunctionTable()
    {
        uint major = 0;
        uint minor = 0;
        uint revision = 0;

        MiniAudio.Version(ref major, ref minor, ref revision);
        string version = MiniAudio.VersionStringS();

        m_output.WriteLine($"miniaudio version: {version}");
        Assert.Equal((uint)0, major);
        Assert.Equal((uint)11, minor);
        Assert.Equal((uint)25, revision);
        Assert.Equal("0.11.25", version);
        Assert.Equal("miniaudio", MiniAudio.GetLibraryName());
    }

    [Fact]
    public void EngineInitializesWithoutAnAudioDevice()
    {
        MaEngineConfig config = MiniAudio.EngineConfigInit();
        config.NoDevice = 1;
        config.Channels = 2;
        config.SampleRate = 48_000;
        MaEngine engine = default;

        MaResult result = MiniAudio.EngineInit(config, ref engine);
        Assert.Equal(MaResult.Success, result);

        MiniAudio.EngineUninit(ref engine);
    }
}
