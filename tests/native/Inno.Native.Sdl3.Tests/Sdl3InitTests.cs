using Xunit;
using Xunit.Abstractions;

namespace Inno.Native.Sdl3.Tests;

public sealed class Sdl3InitTests
{
    private readonly ITestOutputHelper output;

    public Sdl3InitTests(ITestOutputHelper output)
    {
        this.output = output;
    }

    [Fact]
    public void InitAndQuit_ShouldSucceed()
    {
        var initResult = SDL.Init((uint)SDLInitFlags.Events);
        if (!initResult)
        {
            var error = SDL.GetErrorAsException();
            var message = error?.Message ?? "SDL.Init returned false.";
            Assert.True(initResult, message);
        }

        var platform = SDL.GetPlatformS();
        output.WriteLine($"SDL.GetPlatform: {platform}");
        Assert.False(string.IsNullOrWhiteSpace(platform));

        var version = SDL.GetVersion();
        var major = version / 1000000;
        var minor = (version / 1000) % 1000;
        var patch = version % 1000;
        output.WriteLine($"SDL.GetVersion: {major}.{minor}.{patch} ({version})");
        Assert.True(version > 0);

        SDL.Quit();
    }
}
