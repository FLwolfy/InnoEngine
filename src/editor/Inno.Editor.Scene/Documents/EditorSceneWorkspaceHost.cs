using System;

using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Types;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Interactions;
using Inno.Runtime;

namespace Inno.Editor.Scene;

/// <summary>
/// Owns an editor scene workspace created outside the attribute-discovered editor application.
/// </summary>
public sealed class EditorSceneWorkspaceHost : IDisposable
{
    private readonly EditorSceneWorkspace m_workspace;
    private bool m_disposed;

    internal EditorSceneWorkspaceHost(
        RuntimeSession runtimeSession,
        AssetPipeline assets,
        TypeCatalog types,
        SerializationRegistry serialization,
        LogRouter logs,
        IEditorSelectionCoordinator? selection)
    {
        m_workspace = new EditorSceneWorkspace(
            runtimeSession,
            assets,
            types,
            serialization,
            new EditorReloadCoordinator(),
            logs,
            selection);
    }

    /// <summary>
    /// Gets the Edit-or-Play scene presentation and persistence boundary owned by this host.
    /// </summary>
    public IEditorSceneWorkspace workspace => m_workspace;

    /// <summary>
    /// Gets the Edit-or-Play rendering presentation boundary owned by this host.
    /// </summary>
    public IEditorGameScenePresentation gamePresentation => m_workspace;

    /// <summary>
    /// Gets the isolated Play Mode scene-session boundary owned by this host.
    /// </summary>
    public IEditorScenePlayMode playMode => m_workspace;

    /// <summary>
    /// Releases the workspace and any isolated scene session that it still owns.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_workspace.DisposeUnattached();
        m_disposed = true;
    }
}
