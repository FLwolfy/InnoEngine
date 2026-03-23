using System;
using System.IO;
using System.Runtime.InteropServices;
using Inno.Graphics;
using Inno.Graphics.Bgfx;
using Inno.Native.Bgfx;
using Inno.Platform;
using Inno.Platform.SDL3;

namespace Inno.Demo.Window;

internal static class Program
{
    private static int Main()
    {
        using var app = new Sdl3PlatformApplication();
        using var window = app.CreateWindow(new PlatformWindowOptions
        {
            title = "Inno Demo - Triangle",
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

        using var commandList = device.CreateCommandList();
        using var vertexBuffer = device.CreateBuffer(new BufferDescription
        {
            sizeInBytes = Marshal.SizeOf<TriangleVertex>() * 3,
            usage = GraphicsBufferUsage.Vertex,
            cpuAccess = BufferCpuAccess.Write
        });
        vertexBuffer.SetData([
            new TriangleVertex(0.0f, 0.6f, 0.0f, 0xff4c4cff),
            new TriangleVertex(0.6f, -0.6f, 0.0f, 0xff4cff4c),
            new TriangleVertex(-0.6f, -0.6f, 0.0f, 0xffff4c4c)
        ]);

        using var inputLayout = device.CreateInputLayout(new GraphicsInputLayoutDescription
        {
            stride = Marshal.SizeOf<TriangleVertex>(),
            elements =
            [
                new GraphicsVertexElement
                {
                    semantic = "POSITION",
                    semanticIndex = 0,
                    format = VertexFormat.Float3,
                    offset = 0
                },
                new GraphicsVertexElement
                {
                    semantic = "COLOR",
                    semanticIndex = 0,
                    format = VertexFormat.Byte4Normalized,
                    offset = 12
                }
            ]
        });

        var shaderRoot = ResolveShaderRoot(device.rendererType);
        using var vertexShader = device.CreateShader(new ShaderDescription
        {
            stage = ShaderStage.Vertex,
            language = ShaderLanguage.Glsl,
            bytecode = File.ReadAllBytes(Path.Combine(shaderRoot, "vs_cubes.bin"))
        });
        using var fragmentShader = device.CreateShader(new ShaderDescription
        {
            stage = ShaderStage.Fragment,
            language = ShaderLanguage.Glsl,
            bytecode = File.ReadAllBytes(Path.Combine(shaderRoot, "fs_cubes.bin"))
        });
        using var program = device.CreateProgram(new GraphicsProgramDescription
        {
            shaders = [vertexShader, fragmentShader]
        });
        using var pipeline = device.CreateRenderPipeline(new GraphicsRenderPipelineDescription
        {
            program = program,
            inputLayout = inputLayout,
            rasterState = new GraphicsRasterState
            {
                cullMode = GraphicsCullMode.Back,
                frontFaceCounterClockwise = false
            },
            depthState = new GraphicsDepthState
            {
                depthTestEnabled = false,
                depthWriteEnabled = false
            }
        });
        IGraphicsRenderTarget renderTarget = device.CreateRenderTarget(new GraphicsRenderTargetDescription
        {
            width = window.width,
            height = window.height,
            colorFormats = [PixelFormat.B8G8R8A8Unorm],
            depthFormat = PixelFormat.D24UnormS8Uint
        });

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
                    swapchain.Resize(evnt.width, evnt.height);
                    renderTarget.Dispose();
                    renderTarget = device.CreateRenderTarget(new GraphicsRenderTargetDescription
                    {
                        width = evnt.width,
                        height = evnt.height,
                        colorFormats = [PixelFormat.B8G8R8A8Unorm],
                        depthFormat = PixelFormat.D24UnormS8Uint
                    });
                }
            }

            if (!isRunning)
            {
                break;
            }

            device.BeginFrame();

            commandList.Begin();
            commandList.BeginRenderPass(renderTarget, new ClearValue(0.1f, 0.12f, 0.16f, 1.0f));
            commandList.SetViewport(new GraphicsViewport(0, 0, window.width, window.height));
            commandList.SetPipeline(pipeline);
            commandList.SetVertexBuffer(vertexBuffer, 0);
            commandList.Draw(3);
            commandList.EndRenderPass();
            commandList.End();

            device.Submit(commandList);
            device.EndFrame();
        }

        device.WaitIdle();
        renderTarget.Dispose();
        return 0;
    }

    private static string ResolveShaderRoot(bgfx.RendererType rendererType)
    {
        var profile = rendererType switch
        {
            bgfx.RendererType.Metal => "metal",
            bgfx.RendererType.Vulkan => "spirv",
            bgfx.RendererType.OpenGL => "glsl",
            bgfx.RendererType.OpenGLES => "essl",
            bgfx.RendererType.Direct3D11 => "dxbc",
            bgfx.RendererType.Direct3D12 => "dxil",
            bgfx.RendererType.WebGPU => "wgsl",
            _ => "glsl"
        };

        var root = FindRepoRoot(AppContext.BaseDirectory);
        var path = Path.Combine(root, "extern", "bgfx", "examples", "runtime", "shaders", profile);
        if (!Directory.Exists(path))
        {
            throw new DirectoryNotFoundException($"Cannot find bgfx shader directory: {path}");
        }

        return path;
    }

    private static string FindRepoRoot(string startPath)
    {
        var current = new DirectoryInfo(startPath);
        while (current is not null)
        {
            var candidate = Path.Combine(current.FullName, "extern", "bgfx", "examples", "runtime", "shaders");
            if (Directory.Exists(candidate))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("Cannot locate repository root from current runtime path.");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct TriangleVertex
    {
        public readonly float x;
        public readonly float y;
        public readonly float z;
        public readonly uint abgr;

        public TriangleVertex(float x, float y, float z, uint abgr)
        {
            this.x = x;
            this.y = y;
            this.z = z;
            this.abgr = abgr;
        }
    }
}
