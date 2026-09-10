using System;
using System.IO;

namespace Inno.Build.Toolchains.ImGui;

internal static class CimguiSourceOverlay
{
    internal static string Prepare(string cimguiDirectory)
    {
        string source = File.ReadAllText(Path.Combine(cimguiDirectory, "imgui", "imgui.cpp"));
        source = ReplaceOnce(source,
            "ImRect bg_rect(window->Pos + ImVec2(0, window->TitleBarHeight), window->Pos + window->Size);",
            "// Keep one continuous backing surface under the title and body. Independently anti-aliased shared edges leak the window behind them.\n" +
            "                ImRect bg_rect(window->DockIsActive ? window->Pos + ImVec2(0, window->TitleBarHeight) : window->Pos, window->Pos + window->Size);");
        source = ReplaceOnce(source,
            "bg_rounding_flags = (flags & ImGuiWindowFlags_NoTitleBar) ? ImDrawFlags_RoundCornersAll : ImDrawFlags_RoundCornersBottom;",
            "bg_rounding_flags = ImDrawFlags_RoundCornersAll;");

        string directory = Path.Combine(cimguiDirectory, CimguiBuildConstants.BUILD_DIR_NAME, "inno-source");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "imgui.cpp"), source);
        File.Copy(Path.Combine(AppContext.BaseDirectory, "CMakeLists.txt"), Path.Combine(directory, "CMakeLists.txt"), overwrite: true);
        return directory;
    }

    private static string ReplaceOnce(string source, string expected, string replacement)
    {
        int index = source.IndexOf(expected, StringComparison.Ordinal);
        if (index < 0 || source.IndexOf(expected, index + expected.Length, StringComparison.Ordinal) >= 0)
            throw new InvalidOperationException("The cimgui window-background source no longer matches its validated overlay. Review the upstream change before building.");
        return string.Concat(source.AsSpan(0, index), replacement, source.AsSpan(index + expected.Length));
    }
}
