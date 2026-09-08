using System;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Extensibility.Types;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Editor.Interactions;
using Inno.Runtime;

namespace Inno.Editor.Scene;

/// <summary>
/// Creates explicitly owned editor scene workspaces for embedded editor hosts and command-line tooling.
/// </summary>
public static class EditorSceneWorkspaceFactory
{
    /// <summary>
    /// Creates an unattached workspace over explicitly owned Edit-session services.
    /// </summary>
    /// <param name="runtimeSession">
    /// The Edit session that owns the workspace scene world.
    /// </param>
    /// <param name="assets">
    /// The authoring asset pipeline used for scene and prefab documents.
    /// </param>
    /// <param name="types">
    /// The host type catalog used for scene extension generations.
    /// </param>
    /// <param name="serialization">
    /// The host serialization registry used for scene snapshots.
    /// </param>
    /// <param name="logs">
    /// The application log router used for workspace lifecycle diagnostics.
    /// </param>
    /// <param name="selection">
    /// The optional selection coordinator used by the standalone document workspace.
    /// </param>
    /// <returns>
    /// A host that owns both the document and Play Mode interfaces of the workspace.
    /// </returns>
    /// <exception cref="ArgumentNullException">
    /// Thrown when a required owner is <see langword="null"/>.
    /// </exception>
    public static EditorSceneWorkspaceHost Create(
        RuntimeSession runtimeSession,
        AssetPipeline assets,
        TypeCatalog types,
        SerializationRegistry serialization,
        LogRouter logs,
        IEditorSelectionCoordinator? selection = null)
        => new(runtimeSession, assets, types, serialization, logs, selection);
}
