using System;
using System.IO;

namespace Inno.Build.Toolchains.ImGuizmo;

internal static class CImguizmoBuildUtils
{
    /// <summary>
    /// Validates the supplied input and rejects state that cannot satisfy this contract.
    /// </summary>
    /// <param name="cimguizmoDir">
    /// The cimguizmo dir text validated by the validate source operation.
    /// </param>
    /// <param name="cimguiDir">
    /// The cimgui dir text validated by the validate source operation.
    /// </param>
    public static void ValidateSource(string cimguizmoDir, string cimguiDir)
    {
        if (!Directory.Exists(cimguizmoDir))
        {
            throw new DirectoryNotFoundException(
                $"cimguizmo source not found at {cimguizmoDir}. Place the cimguizmo repository in extern/cimguizmo before running this tool.");
        }

        if (!Directory.Exists(cimguiDir))
        {
            throw new DirectoryNotFoundException(
                $"cimgui source not found at {cimguiDir}. Place the cimgui repository in extern/cimgui before running this tool.");
        }

        var cimguizmoCpp = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.CIMGUIMO_CPP_FILE);
        if (!File.Exists(cimguizmoCpp))
        {
            throw new FileNotFoundException($"Missing {CImguizmoBuildConstants.CIMGUIMO_CPP_FILE}: {cimguizmoCpp}");
        }

        var imguizmoDir = Path.Combine(cimguizmoDir, CImguizmoBuildConstants.IMGUIZMO_DIR_NAME);
        var imguizmoCpp = Path.Combine(imguizmoDir, CImguizmoBuildConstants.IMGUIZMO_CPP_FILE);
        if (!File.Exists(imguizmoCpp))
        {
            throw new FileNotFoundException($"Missing {CImguizmoBuildConstants.IMGUIZMO_CPP_FILE}: {imguizmoCpp}");
        }
    }
}
