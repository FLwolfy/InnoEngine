using System;
using System.IO;

using Inno.Assets.Plugins;
using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.Panel.FileBrowser;

/// <summary>Owns the transient authoring-to-installation export session.</summary>
[EditorModule("plugin-export", order: 110)]
internal sealed class PluginExportWindowModule : EditorModule
{
    private PluginDefinitionAsset? m_definition;
    private PluginExportKind m_kind;
    private string m_projectDirectory = string.Empty;
    private string m_outputPath = string.Empty;
    private string m_error = string.Empty;

    internal bool isVisible => m_definition is not null;

    internal PluginExportKind kind => m_kind;

    internal string pluginId => m_definition?.pluginId ?? string.Empty;

    internal string outputPath
    {
        get => m_outputPath;
        set
        {
            string next = value ?? string.Empty;
            if (string.Equals(m_outputPath, next, StringComparison.Ordinal))
                return;
            m_outputPath = next;
            m_error = string.Empty;
        }
    }

    internal string error => m_error;

    internal void Open(
        PluginDefinitionAsset definition,
        PluginExportKind kind,
        string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(projectDirectory);
        m_definition = definition;
        m_kind = kind;
        m_projectDirectory = Path.GetFullPath(projectDirectory);
        m_outputPath = Path.Combine(
            m_projectDirectory,
            definition.pluginId + (kind == PluginExportKind.Zip ? ".zip" : string.Empty));
        m_error = string.Empty;
    }

    internal void Export()
    {
        if (m_definition is null)
            return;
        try
        {
            string destination = ValidateDestination(m_outputPath, m_kind, m_projectDirectory);
            string hash = m_kind == PluginExportKind.Zip
                ? PluginExportService.ExportZip(m_definition, destination)
                : PluginExportService.ExportDirectory(m_definition, destination);
            Log.Info(
                "Exported Plugin installation '{0}' to '{1}' ({2}).",
                m_definition.pluginId,
                destination,
                hash);
            Close();
        }
        catch (Exception exception)
        {
            m_error = exception.Message;
            Log.Error("Plugin export failed: {0}", exception);
        }
    }

    internal void Close()
    {
        m_definition = null;
        m_projectDirectory = string.Empty;
        m_outputPath = string.Empty;
        m_error = string.Empty;
    }

    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
        => Close();

    private static string ValidateDestination(
        string outputPath,
        PluginExportKind kind,
        string projectDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        string destination = Path.GetFullPath(outputPath);
        if (kind == PluginExportKind.Zip &&
            !string.Equals(Path.GetExtension(destination), ".zip", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("A Plugin ZIP export must use the .zip extension.");
        }

        string[] managedRoots =
        [
            Path.Combine(projectDirectory, "Assets"),
            Path.Combine(projectDirectory, "Plugins"),
            Path.Combine(projectDirectory, "Library")
        ];
        foreach (string root in managedRoots)
        {
            if (IsWithin(destination, root))
            {
                throw new InvalidOperationException(
                    $"Export outside the current project's '{Path.GetFileName(root)}' root. " +
                    "Copy the completed installation into another project's Plugins directory.");
            }
        }
        return destination;
    }

    private static bool IsWithin(string path, string root)
    {
        string normalizedRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        StringComparison comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(path, normalizedRoot, comparison) ||
               path.StartsWith(normalizedRoot + Path.DirectorySeparatorChar, comparison);
    }
}
