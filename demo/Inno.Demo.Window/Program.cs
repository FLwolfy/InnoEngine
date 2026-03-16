using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Inno.Native.Bgfx;
using Inno.Native.SDL3;

namespace Inno.Demo.Window;

static class Program
{
    private const int WINDOW_WIDTH = 1280;
    private const int WINDOW_HEIGHT = 720;
    private const ushort VIEW_ID = 0;

    public static unsafe int Main(string[] args)
    {
        if (!OperatingSystem.IsMacOS())
        {
            Console.Error.WriteLine("Inno.Demo.Window currently supports macOS only.");
            return 1;
        }

        if (!SDL.Init((uint)SDLInitFlags.Video))
        {
            var message = SDL.GetErrorAsException()?.Message ?? "SDL.Init failed.";
            Console.Error.WriteLine(message);
            return 1;
        }

        var window = SDL.CreateWindow("Inno SDL3 + bgfx", WINDOW_WIDTH, WINDOW_HEIGHT, (ulong)(SDLWindowFlags.Metal | SDLWindowFlags.Resizable));
        if (window.IsNull)
        {
            var message = SDL.GetErrorAsException()?.Message ?? "SDL.CreateWindow failed.";
            Console.Error.WriteLine(message);
            SDL.Quit();
            return 1;
        }

        var metalView = SDL.MetalCreateView(window);
        var metalLayer = SDL.MetalGetLayer(metalView);
        if (metalLayer == null)
        {
            Console.Error.WriteLine("SDL_Metal_GetLayer returned null.");
            SDL.DestroyWindow(window);
            SDL.Quit();
            return 1;
        }

        bgfx.Init init;
        bgfx.init_ctor(&init);
        init.type = bgfx.RendererType.Metal;
        init.platformData.nwh = metalLayer;
        init.platformData.ndt = null;
        init.platformData.type = bgfx.NativeWindowHandleType.Default;
        init.resolution.width = WINDOW_WIDTH;
        init.resolution.height = WINDOW_HEIGHT;
        init.resolution.reset = (uint)bgfx.ResetFlags.Vsync;

        if (!bgfx.init(&init))
        {
            Console.Error.WriteLine("bgfx.init failed.");
            SDL.MetalDestroyView((nint)metalView);
            SDL.DestroyWindow(window);
            SDL.Quit();
            return 1;
        }

        bgfx.set_view_clear(VIEW_ID, (ushort)(bgfx.ClearFlags.Color | bgfx.ClearFlags.Depth), 0x303030ff, 1.0f, 0);
        bgfx.set_view_rect(VIEW_ID, 0, 0, (ushort)WINDOW_WIDTH, (ushort)WINDOW_HEIGHT);

        bgfx.VertexLayout layout;
        bgfx.vertex_layout_begin(&layout, bgfx.RendererType.Metal);
        bgfx.vertex_layout_add(&layout, bgfx.Attrib.Position, 3, bgfx.AttribType.Float, false, false);
        bgfx.vertex_layout_add(&layout, bgfx.Attrib.Color0, 4, bgfx.AttribType.Uint8, true, true);
        bgfx.vertex_layout_end(&layout);

        var vertices = new[]
        {
            new PosColorVertex(0.0f, 0.5f, 0.0f, 0xff0000ff),
            new PosColorVertex(-0.5f, -0.5f, 0.0f, 0xff00ff00),
            new PosColorVertex(0.5f, -0.5f, 0.0f, 0xffff0000),
        };

        var indices = new ushort[] { 0, 1, 2 };

        bgfx.VertexBufferHandle vbh;
        bgfx.IndexBufferHandle ibh;

        fixed (PosColorVertex* v = vertices)
        {
            var mem = bgfx.copy(v, (uint)(sizeof(PosColorVertex) * vertices.Length));
            vbh = bgfx.create_vertex_buffer(mem, &layout, 0);
        }

        fixed (ushort* i = indices)
        {
            var mem = bgfx.copy(i, (uint)(sizeof(ushort) * indices.Length));
            ibh = bgfx.create_index_buffer(mem, 0);
        }

        var (vsPath, fsPath) = GetShaderPaths();
        var program = LoadProgram(vsPath, fsPath);

        var identity = new float[]
        {
            1f, 0f, 0f, 0f,
            0f, 1f, 0f, 0f,
            0f, 0f, 1f, 0f,
            0f, 0f, 0f, 1f,
        };

        var running = true;
        var start = DateTime.UtcNow;

        fixed (float* view = identity)
        fixed (float* proj = identity)
        {
            bgfx.set_view_transform(VIEW_ID, view, proj);

            while (running)
            {
                SDLEvent evnt = default;
                while (SDL.PollEvent(ref evnt))
                {
                    if ((SDLEventType)evnt.Type == SDLEventType.Quit)
                    {
                        running = false;
                        break;
                    }
                }

                bgfx.touch(VIEW_ID);
                bgfx.set_transform(view, 1);
                bgfx.set_vertex_buffer(0, vbh, 0, 3);
                bgfx.set_index_buffer(ibh, 0, 3);
                bgfx.set_state((ulong)(bgfx.StateFlags.WriteRgb | bgfx.StateFlags.WriteA), 0);
                bgfx.submit(VIEW_ID, program, 0, 0);
                bgfx.frame((byte)bgfx.FrameFlags.None);

                Thread.Sleep(16);

                if ((DateTime.UtcNow - start).TotalSeconds > 5)
                {
                    running = false;
                }
            }
        }

        bgfx.destroy_program(program);
        bgfx.destroy_index_buffer(ibh);
        bgfx.destroy_vertex_buffer(vbh);
        bgfx.shutdown();

        SDL.MetalDestroyView((nint)metalView);
        SDL.DestroyWindow(window);
        SDL.Quit();
        return 0;
    }

    private static (string VertexShader, string FragmentShader) GetShaderPaths()
    {
        var repoRoot = FindRepoRoot();
        var shaderDir = Path.Combine(repoRoot, "extern", "bgfx", "examples", "runtime", "shaders", "metal");
        return (Path.Combine(shaderDir, "vs_cubes.bin"), Path.Combine(shaderDir, "fs_cubes.bin"));
    }

    private static bgfx.ProgramHandle LoadProgram(string vsPath, string fsPath)
    {
        if (!File.Exists(vsPath) || !File.Exists(fsPath))
        {
            throw new FileNotFoundException($"Shader binaries not found: {vsPath}, {fsPath}");
        }

        var vsh = CreateShader(File.ReadAllBytes(vsPath));
        var fsh = CreateShader(File.ReadAllBytes(fsPath));
        return bgfx.create_program(vsh, fsh, true);
    }

    private static bgfx.ShaderHandle CreateShader(byte[] data)
    {
        unsafe
        {
            fixed (byte* ptr = data)
            {
                var mem = bgfx.copy(ptr, (uint)data.Length);
                return bgfx.create_shader(mem);
            }
        }
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var marker = Path.Combine(dir.FullName, "InnoEngine.sln");
            if (File.Exists(marker))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Repo root not found (InnoEngine.sln missing).");
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private readonly struct PosColorVertex
    {
        public readonly float X;
        public readonly float Y;
        public readonly float Z;
        public readonly uint Abgr;

        public PosColorVertex(float x, float y, float z, uint abgr)
        {
            X = x;
            Y = y;
            Z = z;
            Abgr = abgr;
        }
    }
}
