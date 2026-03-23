using Inno.Core.Mathematics;
using Inno.Graphics;
using Inno.Graphics.Bgfx;
using Inno.Platform;
using Inno.Platform.SDL3;
using Inno.Rendering;
using System.Diagnostics;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        using var app = new Sdl3PlatformApplication();
        using var window = app.CreateWindow(new PlatformWindowOptions
        {
            title = "Inno Demo",
            width = 1280,
            height = 720,
            resizable = true,
            highPixelDensity = true
        });

        using var device = new BgfxGraphicsDevice();
        using var swapchain = device.CreateSwapchain(new GraphicsSwapchainDescription
        {
            nativeHandle = window.nativeHandles.windowHandle,
            nativeDisplayHandle = window.nativeHandles.displayHandle,
            nativeWindowKind = window.nativeHandles.handleKind switch
            {
                PlatformNativeHandleKind.Win32 => GraphicsNativeWindowKind.Win32,
                PlatformNativeHandleKind.Cocoa => GraphicsNativeWindowKind.Cocoa,
                PlatformNativeHandleKind.Wayland => GraphicsNativeWindowKind.Wayland,
                PlatformNativeHandleKind.X11 => GraphicsNativeWindowKind.X11,
                _ => GraphicsNativeWindowKind.Unknown
            },
            width = window.width,
            height = window.height,
            colorFormat = PixelFormat.B8G8R8A8Unorm,
            depthFormat = PixelFormat.D24UnormS8Uint,
            vSync = true
        });

        var renderWindow = new RenderWindow
        {
            nativeHandle = window.nativeHandles.windowHandle,
            width = window.width,
            height = window.height
        };

        var renderTarget = RenderTarget.Backbuffer(renderWindow);
        var pipeline = ForwardPipeline.Create(builder =>
        {
            builder.enableDepthPrepass = true;
            builder.enableShadows = true;
            builder.enableTransparentPass = true;
            builder.enableSkybox = false;
            builder.enablePostProcessing = false;
            builder.enableUiPass = false;
        });

        using var renderSystem = new RenderSystem(
            device,
            swapchain,
            pipeline,
            new RenderSettings
            {
                enableValidation = true,
                collectStatistics = true
            });

        var scene = new RenderScene();
        scene.environment.ambientColor = new Color(0.08f, 0.09f, 0.11f, 1.0f);
        scene.environment.ambientIntensity = 0.2f;
        scene.settings.enableShadows = true;

        scene.Add(new DirectionalLight
        {
            color = Color.WHITE,
            intensity = 1.5f,
            direction = Vector3.NormalizeSafe(new Vector3(-0.5f, -1.0f, -0.3f)),
            shadows = LightShadowSettings.@default with { enabled = true }
        });

        var material = new CustomMaterial
        {
            name = "CubeCustom",
            shaderName = "cubes",
            surfaceType = MaterialSurfaceType.Opaque,
            cullMode = MaterialCullMode.Back,
            depthMode = MaterialDepthMode.ReadWrite
        };

        var cube = new MeshRenderable
        {
            name = "Cube",
            mesh = CreateCubeMesh(),
            material = material,
            transform = Transform.identity
        };
        scene.Add(cube);

        var camera = new PerspectiveCamera
        {
            fieldOfViewDegrees = 60.0f,
            nearClip = 0.1f,
            farClip = 100.0f,
            transform = new CameraTransform
            {
                position = new Vector3(0.0f, 1.0f, 4.5f),
                rotation = Quaternion.identity
            }
        };

        var view = RenderView.ForCamera(camera)
            .WithViewport(0, 0, window.width, window.height)
            .WithClear(ClearSettings.Solid(new Color(0.08f, 0.09f, 0.11f, 1.0f)));

        var timer = Stopwatch.StartNew();
        var isRunning = true;
        while (isRunning && !window.isClosed)
        {
            while (app.PollEvent(out var evnt))
            {
                if (evnt.type == PlatformEventType.QuitRequested || evnt.type == PlatformEventType.WindowCloseRequested)
                {
                    isRunning = false;
                    break;
                }

                if (evnt.type == PlatformEventType.WindowResized && evnt.width > 0 && evnt.height > 0)
                {
                    renderWindow.width = evnt.width;
                    renderWindow.height = evnt.height;
                    view.WithViewport(0, 0, evnt.width, evnt.height);
                }
            }

            if (!isRunning)
            {
                break;
            }

            var t = (float)timer.Elapsed.TotalSeconds;
            cube.transform = new Transform
            {
                position = new Vector3(0.0f, 0.0f, 0.0f),
                rotation = Quaternion.CreateFromYawPitchRoll(t * 0.7f, t * 0.35f, t * 0.15f),
                scale = Vector3.ONE
            };

            var request = new RenderRequest
            {
                scene = scene,
                view = view,
                target = renderTarget
            };
            renderSystem.Render(request);
        }

        return 0;
    }

    private static Mesh CreateCubeMesh()
    {
        var s = 0.75f;
        var vertices = new[]
        {
            new StandardVertex { position = new Vector3(-s, -s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(1, 0, 0, 1) },
            new StandardVertex { position = new Vector3(+s, -s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(0, 1, 0, 1) },
            new StandardVertex { position = new Vector3(+s, +s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0, 0, 1, 1) },
            new StandardVertex { position = new Vector3(-s, +s, +s), normal = Vector3.BACK, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 0, 1) },
            new StandardVertex { position = new Vector3(-s, -s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(1, 0, 1, 1) },
            new StandardVertex { position = new Vector3(+s, -s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(0, 1, 1, 1) },
            new StandardVertex { position = new Vector3(+s, +s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(1, 1, 1, 1) },
            new StandardVertex { position = new Vector3(-s, +s, -s), normal = Vector3.FORWARD, tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0, 0, 0, 1) }
        };

        uint[] indices =
        [
            0, 1, 2, 2, 3, 0,
            1, 5, 6, 6, 2, 1,
            5, 4, 7, 7, 6, 5,
            4, 0, 3, 3, 7, 4,
            3, 2, 6, 6, 7, 3,
            4, 5, 1, 1, 0, 4
        ];

        return new MeshBuilder()
            .SetVertices<StandardVertex>(vertices)
            .SetIndices(indices)
            .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
            .Build("DemoCube");
    }
}
