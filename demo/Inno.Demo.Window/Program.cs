using System;
using Inno.Core.Framework;
using Inno.Core.Mathematics;
using Inno.Graphics;
using Inno.Graphics.Rhi;
using Inno.Graphics.Rhi.Bgfx;
using Inno.Platform;
using Inno.Platform.SDL3;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("Inno.Demo.Window currently supports macOS only.");
            return 1;
        }

        var windowOptions = new WindowCreateOptions
        {
            title = "InnoEngine - 2D Demo",
            width = 1280,
            height = 720,
            resizable = true,
        };

        var graphicsOptions = new GraphicsOptions
        {
            backend = GraphicsBackend.Auto,
            enableVsync = true,
            defaultClearColor = Color.DARKGRAY,
            defaultClearDepth = 1.0f,
        };

        using var platform = Sdl3PlatformFactory.CreateRuntime();
        using var window = platform.CreateWindow(windowOptions);
        BgfxRhiFactory.Install(window, new RhiDeviceCreateOptions
        {
            backend = graphicsOptions.backend switch
            {
                GraphicsBackend.Noop => RhiBackend.Noop,
                GraphicsBackend.Direct3D11 => RhiBackend.Direct3D11,
                GraphicsBackend.Direct3D12 => RhiBackend.Direct3D12,
                GraphicsBackend.Metal => RhiBackend.Metal,
                GraphicsBackend.OpenGles => RhiBackend.OpenGles,
                GraphicsBackend.OpenGl => RhiBackend.OpenGl,
                GraphicsBackend.Vulkan => RhiBackend.Vulkan,
                GraphicsBackend.WebGpu => RhiBackend.WebGpu,
                _ => RhiBackend.Auto,
            },
            viewId = graphicsOptions.view2DId,
            enableVsync = graphicsOptions.enableVsync,
        });
        using var graphics = GraphicsFactory.Create2D();
        using var shell = new Shell(maxFrameRate: 240);

        shell.SetOnStep(() =>
        {
            while (platform.PumpEvents(out var evt))
            {
                if (evt.type == PlatformEventType.QuitRequested || evt.type == PlatformEventType.WindowCloseRequested)
                {
                    shell.Terminate();
                    continue;
                }

                if (evt.type == PlatformEventType.WindowResized)
                {
                    graphics.Resize((uint)Math.Max(1, evt.width), (uint)Math.Max(1, evt.height));
                }
            }

            if (window.isCloseRequested)
            {
                shell.Terminate();
            }
        });

        shell.SetOnDraw(() =>
        {
            graphics.BeginFrame(new FrameRenderOptions
            {
                clearColor = graphicsOptions.defaultClearColor,
                clearDepth = graphicsOptions.defaultClearDepth,
            });
            float t = Time.time;

            float centerX = window.width * 0.5f;
            float centerY = window.height * 0.5f;
            float x = centerX + MathF.Cos(t * 1.2f) * 260f;
            float y = centerY + MathF.Sin(t * 1.8f) * 130f;
            float rotation = t * 1.3f;

            Camera2D camera2D = Camera2D.CreateScreenSpace(window.width, window.height);
            graphics.renderer2D.Begin(camera2D);
            graphics.renderer2D.DrawLine(new Line2D(
                start: new Vector2(0f, centerY),
                end: new Vector2(window.width, centerY),
                color: Color.FromBytes(72, 72, 72)));
            graphics.renderer2D.DrawLine(new Line2D(
                start: new Vector2(centerX, 0f),
                end: new Vector2(centerX, window.height),
                color: Color.FromBytes(72, 72, 72)));
            graphics.renderer2D.DrawQuad(new Quad2D(
                position: new Vector2(x, y),
                size: new Vector2(160f, 110f),
                color: Color.FromBytes(80, 170, 255),
                rotationRadians: rotation,
                depth: 0f));
            graphics.renderer2D.DrawTriangle(new Triangle2D(
                a: new Vector2(980f, 160f),
                b: new Vector2(1140f, 300f),
                c: new Vector2(860f, 300f),
                color: Color.FromBytes(255, 120, 120),
                depth: 0f));
            graphics.renderer2D.DrawCircle(new Circle2D(
                center: new Vector2(1020f, 520f),
                radius: 72f,
                color: Color.FromBytes(120, 235, 160),
                segments: 48,
                depth: 0f));
            graphics.renderer2D.DrawPolygon(new Polygon2D(
                points:
                [
                    new Vector2(140f, 560f),
                    new Vector2(220f, 500f),
                    new Vector2(300f, 560f),
                    new Vector2(380f, 500f),
                    new Vector2(460f, 560f),
                ],
                color: Color.FromBytes(245, 203, 92),
                filled: false,
                closed: false,
                depth: 0f));
            graphics.renderer2D.DrawQuad(new Quad2D(
                position: new Vector2(centerX, centerY),
                size: new Vector2(50f, 50f),
                color: Color.FromBytes(245, 203, 92),
                rotationRadians: 0f,
                depth: 0f));
            graphics.renderer2D.End();

            graphics.EndFrame();
        });

        shell.Run();
        return 0;
    }
}
