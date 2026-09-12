using System;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Editor.Core;
using Inno.Editor.Diagnostics;
using Inno.Editor.Interactions;
using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Native.ImGui;
using UI = Inno.Native.ImGui.ImGui;
using Xunit;

namespace Inno.Editor.PlayMode.Tests;

public sealed class ConsolePanelNativeLayoutTests
{
    [Theory]
    [InlineData(false, 1f)]
    [InlineData(true, 1f)]
    [InlineData(false, 1.5f)]
    [InlineData(true, 1.5f)]
    public unsafe void RealConsoleKeepsCompleteLabelsOnFirstExpansionAndResize(bool diagnostic, float scale)
    {
        string root = Path.Combine(Path.GetTempPath(), "InnoConsoleLayout", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        _ = Assembly.Load("Inno.Editor.Panel.Logging");
        ImGuiContextPtr context = UI.CreateContext();
        try
        {
            UI.GetIO().DisplaySize = new(1200, 800);
            UI.GetIO().DeltaTime = 1f / 60;
            UI.GetIO().BackendFlags |= ImGuiBackendFlags.RendererHasTextures;
            UI.GetIO().Fonts.RendererHasTextures = true;
            UI.GetStyle().ScaleAllSizes(scale);
            using var modules = new ModuleHost(new() { cacheDirectory = Path.Combine(root, "Library", "Assemblies") });
            using var types = new TypeCatalog(modules);
            using var logs = new LogRouter();
            var hub = new DiagnosticHub();
            using var console = new EditorConsole(logs, hub, new InactivePlayMode());
            console.Start();
            using var reporter = hub.CreateReporter(new("shader", "Rendering"));
            using var runtime = new EditorInteractionRuntime(new EditorContext(root), types, logs, [console]);
            runtime.Start();
            logs.Flush();
            console.Clear();
            if (diagnostic) reporter.Publish(new("BGFX_IR_GENERATION", "BGFX has no position builtin for stage Compute.", DiagnosticSeverity.Error));
            else logs.CreateLogger<ConsolePanelNativeLayoutTests>().Write(LogLevel.Error, "BGFX has no position builtin for stage Compute.");
            logs.Flush();
            EditorPanelExtension panel = Assert.Single(runtime.panels, value => value.id == "diagnostics.console");
            panel.isOpen = true;
            void Draw(float width)
            {
                UI.NewFrame();
                UI.SetNextWindowPos(new(0, 0));
                UI.SetNextWindowSize(new(width, 700));
                _ = UI.Begin("Console Layout");
                Assert.True(panel.Draw(runtime.context));
                UI.End();
                UI.Render();
                Assert.Single(console.Capture().occurrences);
            }
            Draw(960); Draw(960);
            ImGuiWindowPtr card = default;
            for (int i = 0; i < context.Windows.Size; i++)
            {
                ImGuiWindowPtr window = context.Windows[i];
                if (Marshal.PtrToStringUTF8((nint)window.Name)?.Contains("##ConsoleEntryCard", StringComparison.Ordinal) == true) card = window;
            }
            Assert.True(card.Handle != null);
            ImGuiTablePtr header = ImGuiP.TableFindByID(ImGuiP.GetID(card, "##HeaderTable"));
            Assert.True(header.Handle != null);
            Vector2 toggle = new(header.Columns.Data[0].WorkMinX + 6, header.RowPosY1 + UI.GetFontSize() / 2);
            UI.GetIO().AddMousePosEvent(toggle.X, toggle.Y);
            Draw(960);
            Assert.True(context.HoveredId != 0, $"No toggle hovered: {toggle}; window {Marshal.PtrToStringUTF8((nint)context.HoveredWindow.Name)}");
            UI.GetIO().AddMouseButtonEvent(0, true); Draw(960);
            Assert.True(context.ActiveId != 0, "Toggle did not activate.");
            UI.GetIO().AddMouseButtonEvent(0, false); Draw(960);
            // Switching from fixed height to AutoResizeY uses a native measurement frame.
            Draw(960);
            foreach (float width in new[] { 960f, 500f, 260f, 140f, 960f })
            {
                Draw(width);
                uint occurrenceId = ImGuiP.GetID(card, unchecked((int)Assert.Single(console.Capture().occurrences).sequence));
                ImGuiTablePtr table = ImGuiP.TableFindByID(ImGuiP.ImHashStr("##ConsoleEntryDetails", occurrenceId));
                bool details = table.Handle != null && table.LastFrameActive == UI.GetFrameCount();
                if (details)
                {
                    ImGuiTableColumn label = table.Columns.Data[0], value = table.Columns.Data[1];
                    Assert.True(label.WidthGiven >= UI.CalcTextSize("Session:").X - 1);
                    Assert.True(value.MinX > label.MinX + UI.CalcTextSize("Source:").X);
                    Assert.True(label.ClipRect.Max.X >= label.WorkMinX + UI.CalcTextSize("Source:").X);
                }
                if (width >= 500) Assert.True(details, $"Width {width}, details {details}, toggle {toggle}, card {card.Pos} / {card.Size}");
                if (width == 140) Assert.False(details);
            }
        }
        finally { UI.DestroyContext(context); Directory.Delete(root, true); }
    }

    private sealed class InactivePlayMode : IEditorPlayMode
    {
        public EditorPlayModeState state => EditorPlayModeState.Editing;
        public bool isPlaying => false;
        public string? lastFailure => null;
        public LogSessionId activeSessionId => LogSessionId.none;
        public event Action<EditorPlayModeState>? stateChanged { add { } remove { } }
        public bool EnterPlayMode() => false;
        public bool ExitPlayMode() => false;
    }
}
