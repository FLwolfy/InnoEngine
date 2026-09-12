using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Inno.Assets;
using Inno.Native.ImGui;
using Inno.Rendering;
using UI = Inno.Native.ImGui.ImGui;

namespace Inno.Editor.Panel.ShaderEditor;

internal sealed partial class ShaderEditorCanvas
{
    private void Diagnostics()
    {
        if (draft.showDiagnostics)
        {
            draft.showDiagnostics = false;
            draft.diagnosticSource = "";
            draft.diagnosticLines = [];
            draft.diagnosticReadError = "";
            UI.OpenPopup("Compilation Diagnostics");
        }
        UI.SetNextWindowSize(new(760, 540), ImGuiCond.Appearing);
        if (!UI.BeginPopup("Compilation Diagnostics")) return;
        try
        {
            UI.TextUnformatted(draft.compilationStatus);
            UI.Separator();
            if (UI.BeginChild("##diagnostic-list", new Vector2(0, draft.diagnosticSource.Length == 0 && draft.diagnosticReadError.Length == 0 ? -1 : 150)))
            {
                if (draft.diagnostics.Length == 0)
                    UI.TextWrapped(draft.compilationDiagnostics.Length == 0 ? "No compilation diagnostics." : draft.compilationDiagnostics);
                for (int index = 0; index < draft.diagnostics.Length; index++)
                {
                    ShaderDiagnostic diagnostic = draft.diagnostics[index];
                    UI.PushID(index);
                    bool located = diagnostic.location is { line: > 0 };
                    if (located && UI.SmallButton("Locate")) RevealSource(diagnostic.location!.Value);
                    if (located) UI.SameLine();
                    UI.TextWrapped(diagnostic.code + ": " + diagnostic.message);
                    UI.PopID();
                }
            }
            UI.EndChild();
            if (draft.diagnosticReadError.Length != 0) UI.TextWrapped(draft.diagnosticReadError);
            if (draft.diagnosticSource.Length == 0) return;
            UI.Separator();
            UI.TextUnformatted($"{draft.diagnosticSource}:{draft.diagnosticLine}:{draft.diagnosticColumn}");
            if (UI.SmallButton("Copy Location")) UI.SetClipboardText($"{draft.diagnosticSource}:{draft.diagnosticLine}:{draft.diagnosticColumn}");
            if (UI.BeginChild("##diagnostic-source", new(0, -1), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
            {
                if (draft.revealDiagnosticLine)
                {
                    UI.SetScrollY(MathF.Max(0, (draft.diagnosticLine - 1) * UI.GetTextLineHeightWithSpacing() - UI.GetContentRegionAvail().Y * 0.35f));
                    draft.revealDiagnosticLine = false;
                }
                for (int index = 0; index < draft.diagnosticLines.Length; index++)
                {
                    Vector2 position = UI.GetCursorScreenPos();
                    if (index + 1 == draft.diagnosticLine)
                        UI.GetWindowDrawList().AddRectFilled(position, position + new Vector2(MathF.Max(UI.GetContentRegionAvail().X, 1), UI.GetTextLineHeightWithSpacing()),
                            UI.GetColorU32(ImGuiCol.Header));
                    UI.TextUnformatted($"{index + 1,5}  {draft.diagnosticLines[index]}");
                }
            }
            UI.EndChild();
        }
        finally { UI.EndPopup(); }
    }

    private void RevealSource(ShaderSourceLocation location)
    {
        try
        {
            AssetPath path = AssetPath.Parse(location.assetPath);
            var mount = owner.assets.sourceMounts.SingleOrDefault(value => value.id == path.source)
                ?? throw new IOException("The diagnostic source mount is unavailable.");
            string absolute = mount.Resolve(path.localPath);
            string[] lines = File.ReadAllLines(absolute);
            draft.diagnosticSource = path.ToString();
            draft.diagnosticLines = lines;
            draft.diagnosticLine = location.line;
            draft.diagnosticColumn = location.column;
            draft.revealDiagnosticLine = true;
            draft.diagnosticReadError = "";
        }
        catch (Exception failure) when ((failure is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
            && Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
        {
            draft.diagnosticReadError = failure.Message;
            draft.diagnosticSource = "";
            draft.diagnosticLines = [];
        }
    }
}
