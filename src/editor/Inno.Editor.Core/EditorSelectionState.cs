using System;
using System.Diagnostics.CodeAnalysis;

namespace Inno.Editor.Core;

/// <summary>
/// Stores editor-wide selection and asset navigation state.
/// </summary>
public sealed class EditorSelectionState
{
    private object? m_selectedTarget;

    /// <summary>
    /// Gets the currently focused directory path relative to the asset root.
    /// </summary>
    public string currentDirectory { get; private set; } = string.Empty;

    /// <summary>
    /// Gets the selected target, or <see langword="null"/> when nothing is selected.
    /// </summary>
    public object? selectedTarget => m_selectedTarget;

    /// <summary>
    /// Gets the selected asset path when the current target is an asset entry.
    /// </summary>
    public string? selectedPath => (m_selectedTarget as AssetSelectionTarget)?.relativePath;

    /// <summary>
    /// Gets a monotonically increasing selection change version.
    /// </summary>
    public ulong version { get; private set; }

    /// <summary>
    /// Selects a target object.
    /// </summary>
    /// <param name="target">Target to select.</param>
    public void Select(object target)
    {
        ArgumentNullException.ThrowIfNull(target);
        if (Equals(m_selectedTarget, target))
        {
            return;
        }

        m_selectedTarget = target;
        version++;
    }

    /// <summary>
    /// Clears the current selection.
    /// </summary>
    public void Clear()
    {
        if (m_selectedTarget is null)
        {
            return;
        }

        m_selectedTarget = null;
        version++;
    }

    /// <summary>
    /// Tries to read the current target as a requested type.
    /// </summary>
    /// <typeparam name="TTarget">Requested target type.</typeparam>
    /// <param name="target">Typed target when successful.</param>
    /// <returns><see langword="true"/> when the current target is compatible.</returns>
    public bool TryGet<TTarget>([NotNullWhen(true)] out TTarget? target)
    {
        if (m_selectedTarget is TTarget typedTarget)
        {
            target = typedTarget;
            return true;
        }

        target = default;
        return false;
    }

    /// <summary>
    /// Sets the current asset directory.
    /// </summary>
    /// <param name="relativePath">Directory path relative to the asset root.</param>
    public void SetCurrentDirectory(string relativePath)
    {
        currentDirectory = NormalizeRelativePath(relativePath);
    }

    /// <summary>
    /// Selects an asset entry by relative path.
    /// </summary>
    /// <param name="relativePath">Entry path relative to the asset root.</param>
    public void SetSelectedPath(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            Clear();
            return;
        }

        Select(new AssetSelectionTarget(NormalizeRelativePath(relativePath)));
    }

    private static string NormalizeRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        string path = relativePath.Replace('\\', '/').Trim();
        while (path.StartsWith("./", StringComparison.Ordinal))
        {
            path = path[2..];
        }

        while (path.StartsWith("/", StringComparison.Ordinal))
        {
            path = path[1..];
        }

        while (path.EndsWith("/", StringComparison.Ordinal))
        {
            path = path[..^1];
        }

        return path == "." ? string.Empty : path;
    }
}
