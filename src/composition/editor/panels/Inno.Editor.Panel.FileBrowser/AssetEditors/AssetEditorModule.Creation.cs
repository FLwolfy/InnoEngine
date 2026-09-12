using System;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Core.IO;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

public sealed partial class AssetEditorModule
{
    /// <summary>Creates a native or ordinary-text asset source as one recoverable shared-history operation.</summary>
    /// <param name="path">A new file in an existing writable asset directory.</param>
    /// <param name="bytes">Complete source bytes supplied by the feature's native writer.</param>
    /// <returns>The indexed source identity, even when its first import reports a source error.</returns>
    /// <exception cref="IOException">The source already exists, has no existing parent, or cannot be installed.</exception>
    /// <exception cref="InvalidOperationException">The source mount is unavailable/read-only or history cannot retain the change.</exception>
    public AssetFileEntry CreateSource(AssetPath path, ReadOnlySpan<byte> bytes)
    {
        AssetSourceMount mount = m_pipeline.sourceMounts.SingleOrDefault(source => source.id == path.source)
            ?? throw new InvalidOperationException("The asset source mount is unavailable.");
        if (mount.isReadOnly) throw new InvalidOperationException("Installed asset sources are read-only.");
        string destination = mount.Resolve(path.localPath);
        if (!Directory.Exists(Path.GetDirectoryName(destination))) throw new IOException("The asset's parent directory does not exist.");
        if (File.Exists(destination + ".imeta")) throw new IOException("The new asset path already has an identity sidecar.");
        AtomicFile.WriteAllBytes(destination, bytes, overwrite: false);
        try
        {
            _ = m_pipeline.Import(path);
            byte[] archive = AssetSourceArchive.Capture(m_pipeline, path.ToString(), out bool isDirectory);
            var data = new AssetHistoryData(AssetHistoryOperationKind.CreateAsset, path.ToString(), string.Empty, isDirectory, archive);
            var change = new EditorHistoryChange(AssetHistoryKinds.SourceOperation, EditorHistoryPayload.FromBytes(data.Encode()));
            try { m_interactions.history.RecordApplied("Create Asset", change); }
            catch { change.Dispose(); throw; }
        }
        catch
        {
            m_pipeline.Delete(path);
            throw;
        }
        return m_pipeline.TryGetFileSystemEntry(path, out AssetFileEntry entry) ? entry
            : throw new InvalidOperationException("The created asset was not indexed.");
    }
}
