using Inno.Native.Dll;
using Inno.Native.SDL3;
using Xunit;

namespace Inno.Native.SDL3.Tests;

public sealed class Sdl3InitTests
{
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
        SDL.Quit();
    }
}
