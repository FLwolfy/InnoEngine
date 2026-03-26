using System;
using Inno.Core.Mathematics;
using Inno.Graphics;
using Inno.Graphics.Bgfx;
using Inno.Platform;
using Inno.Rendering;
using System.Diagnostics;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        using var app = new PlatformApplication();
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
            builder.enableDepthPrepass = false;
            builder.enableShadows = true;
            builder.enableTransparentPass = false;
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
        scene.environment.ambientIntensity = 0.22f;
        scene.settings.enableShadows = true;

        scene.Add(new DirectionalLight
        {
            color = Color.WHITE,
            intensity = 1.25f,
            direction = Vector3.NormalizeSafe(new Vector3(0.0f, -1.0f, 0.0f)),
            shadows = LightShadowSettings.@default with
            {
                enabled = true,
                resolution = 4096,
                cascadeCount = 1,
                depthBias = 0.00002f,
                strength = 0.7f,
                pcfRadius = 2
            }
        });

        var material = new StandardMaterial
        {
            name = "CubeLit",
            surfaceType = MaterialSurfaceType.Opaque,
            cullMode = MaterialCullMode.Back,
            depthMode = MaterialDepthMode.ReadWrite,
            receiveShadows = false
        };

        var cube = new MeshRenderable
        {
            name = "Cube",
            mesh = CreateCubeMesh(),
            material = material,
            transform = Transform.identity
        };

        var floorMaterial = new StandardMaterial
        {
            name = "Ground",
            surfaceType = MaterialSurfaceType.Opaque,
            cullMode = MaterialCullMode.Back,
            depthMode = MaterialDepthMode.ReadWrite,
            castShadows = false,
            receiveShadows = true
        };
        var floor = new MeshRenderable
        {
            name = "Ground",
            mesh = CreateGroundMesh(),
            material = floorMaterial,
            transform = new Transform
            {
                position = new Vector3(0.0f, -3.6f, 0.0f),
                rotation = Quaternion.identity,
                scale = Vector3.ONE
            }
        };

        var uiMaterial = new SpriteMaterial
        {
            name = "DemoUiPanel",
            surfaceType = MaterialSurfaceType.Transparent,
            blendMode = MaterialBlendMode.Alpha,
            cullMode = MaterialCullMode.None,
            depthMode = MaterialDepthMode.Disabled,
            castShadows = false,
            receiveShadows = false,
            tint = new Color(0.20f, 0.80f, 0.95f, 0.75f)
        };
        var uiPanel = new SpriteRenderable
        {
            name = "DemoUiPanel",
            material = uiMaterial,
            shadowMode = ShadowMode.Off,
            sortingOrder = 10_000,
            transform = new Transform
            {
                position = new Vector3(0.70f, -0.70f, 0.0f),
                rotation = Quaternion.identity,
                scale = new Vector3(0.22f, 0.14f, 1.0f)
            }
        };

        scene.Add(cube);
        scene.Add(floor);
        scene.Add(uiPanel);

        var camera = new PerspectiveCamera
        {
            fieldOfViewDegrees = 60.0f,
            nearClip = 0.1f,
            farClip = 100.0f,
            transform = new CameraTransform
            {
                position = new Vector3(10.0f, 8.0f, 10.0f),
                rotation = Quaternion.CreateFromYawPitchRoll(0.8f, -0.8f, 0.0f)
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
                position = new Vector3(1.5f, 3.0f, 1.5f),
                rotation = Quaternion.CreateFromYawPitchRoll(t * 0.28f, t * 0.14f, t * 0.06f),
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
            // +Z
            new StandardVertex { position = new Vector3(-s, -s, +s), normal = new Vector3(0, 0, 1), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(0.97f, 0.70f, 0.72f, 1) },
            new StandardVertex { position = new Vector3(+s, -s, +s), normal = new Vector3(0, 0, 1), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(0.97f, 0.70f, 0.72f, 1) },
            new StandardVertex { position = new Vector3(+s, +s, +s), normal = new Vector3(0, 0, 1), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0.97f, 0.70f, 0.72f, 1) },
            new StandardVertex { position = new Vector3(-s, +s, +s), normal = new Vector3(0, 0, 1), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(0.97f, 0.70f, 0.72f, 1) },
            // -Z
            new StandardVertex { position = new Vector3(+s, -s, -s), normal = new Vector3(0, 0, -1), tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(0.70f, 0.85f, 0.98f, 1) },
            new StandardVertex { position = new Vector3(-s, -s, -s), normal = new Vector3(0, 0, -1), tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(0.70f, 0.85f, 0.98f, 1) },
            new StandardVertex { position = new Vector3(-s, +s, -s), normal = new Vector3(0, 0, -1), tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0.70f, 0.85f, 0.98f, 1) },
            new StandardVertex { position = new Vector3(+s, +s, -s), normal = new Vector3(0, 0, -1), tangent = new Vector4(-1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(0.70f, 0.85f, 0.98f, 1) },
            // +X
            new StandardVertex { position = new Vector3(+s, -s, +s), normal = new Vector3(1, 0, 0), tangent = new Vector4(0, 0, -1, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(0.76f, 0.95f, 0.76f, 1) },
            new StandardVertex { position = new Vector3(+s, -s, -s), normal = new Vector3(1, 0, 0), tangent = new Vector4(0, 0, -1, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(0.76f, 0.95f, 0.76f, 1) },
            new StandardVertex { position = new Vector3(+s, +s, -s), normal = new Vector3(1, 0, 0), tangent = new Vector4(0, 0, -1, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0.76f, 0.95f, 0.76f, 1) },
            new StandardVertex { position = new Vector3(+s, +s, +s), normal = new Vector3(1, 0, 0), tangent = new Vector4(0, 0, -1, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(0.76f, 0.95f, 0.76f, 1) },
            // -X
            new StandardVertex { position = new Vector3(-s, -s, -s), normal = new Vector3(-1, 0, 0), tangent = new Vector4(0, 0, 1, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(0.98f, 0.92f, 0.67f, 1) },
            new StandardVertex { position = new Vector3(-s, -s, +s), normal = new Vector3(-1, 0, 0), tangent = new Vector4(0, 0, 1, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(0.98f, 0.92f, 0.67f, 1) },
            new StandardVertex { position = new Vector3(-s, +s, +s), normal = new Vector3(-1, 0, 0), tangent = new Vector4(0, 0, 1, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0.98f, 0.92f, 0.67f, 1) },
            new StandardVertex { position = new Vector3(-s, +s, -s), normal = new Vector3(-1, 0, 0), tangent = new Vector4(0, 0, 1, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(0.98f, 0.92f, 0.67f, 1) },
            // +Y
            new StandardVertex { position = new Vector3(-s, +s, +s), normal = new Vector3(0, 1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(0.82f, 0.74f, 0.96f, 1) },
            new StandardVertex { position = new Vector3(+s, +s, +s), normal = new Vector3(0, 1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(0.82f, 0.74f, 0.96f, 1) },
            new StandardVertex { position = new Vector3(+s, +s, -s), normal = new Vector3(0, 1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0.82f, 0.74f, 0.96f, 1) },
            new StandardVertex { position = new Vector3(-s, +s, -s), normal = new Vector3(0, 1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(0.82f, 0.74f, 0.96f, 1) },
            // -Y
            new StandardVertex { position = new Vector3(-s, -s, -s), normal = new Vector3(0, -1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = new Vector4(0.73f, 0.92f, 0.90f, 1) },
            new StandardVertex { position = new Vector3(+s, -s, -s), normal = new Vector3(0, -1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = new Vector4(0.73f, 0.92f, 0.90f, 1) },
            new StandardVertex { position = new Vector3(+s, -s, +s), normal = new Vector3(0, -1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = new Vector4(0.73f, 0.92f, 0.90f, 1) },
            new StandardVertex { position = new Vector3(-s, -s, +s), normal = new Vector3(0, -1, 0), tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = new Vector4(0.73f, 0.92f, 0.90f, 1) }
        };

        uint[] indices =
        [
            0, 1, 2, 2, 3, 0,
            4, 5, 6, 6, 7, 4,
            8, 9, 10, 10, 11, 8,
            12, 13, 14, 14, 15, 12,
            16, 17, 18, 18, 19, 16,
            20, 21, 22, 22, 23, 20
        ];

        return new MeshBuilder()
            .SetVertices<StandardVertex>(vertices)
            .SetIndices(indices)
            .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
            .Build("DemoCube");
    }

    private static Mesh CreateGroundMesh()
    {
        const float h = 5.0f;
        var c = new Vector4(0.78f, 0.80f, 0.82f, 1.0f);
        var vertices = new[]
        {
            new StandardVertex { position = new Vector3(-h, 0.0f, -h), normal = Vector3.UP, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 0), color = c },
            new StandardVertex { position = new Vector3(+h, 0.0f, -h), normal = Vector3.UP, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 0), color = c },
            new StandardVertex { position = new Vector3(+h, 0.0f, +h), normal = Vector3.UP, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(1, 1), color = c },
            new StandardVertex { position = new Vector3(-h, 0.0f, +h), normal = Vector3.UP, tangent = new Vector4(1, 0, 0, 1), texCoord0 = new Vector2(0, 1), color = c }
        };
        uint[] indices = [0, 2, 1, 2, 0, 3];

        return new MeshBuilder()
            .SetVertices<StandardVertex>(vertices)
            .SetIndices(indices)
            .AddSurface(new MeshSurface(0, indices.Length, 0, MeshTopology.Triangles))
            .Build("DemoGround");
    }
}
