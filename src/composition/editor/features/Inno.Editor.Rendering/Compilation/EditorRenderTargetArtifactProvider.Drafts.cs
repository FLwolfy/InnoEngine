using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Inno.Core.Diagnostics;
using Inno.Core.Graphs;
using Inno.Assets;
using Inno.Rendering;
using Inno.Rendering.Assets;

namespace Inno.Editor.Rendering;

public sealed partial class EditorRenderTargetArtifactProvider
{
    private readonly Dictionary<Guid, DraftEntry> m_drafts = [];

    internal EditorShaderDraftCompilationSnapshot RequestDraft(Guid documentId, GraphDocument graph, ulong revision,
        RenderShaderVariant variant, GraphicsCapabilities capabilities)
    {
        if (documentId == Guid.Empty) throw new ArgumentException("A preview requires a document identity.", nameof(documentId));
        ArgumentNullException.ThrowIfNull(graph);
        lock (m_sync)
        {
            EnsureActive();
            ShaderCompileTarget target = m_shaderCompiler.CreateTarget(capabilities, optimize: false, debugInformation: true);
            if (!m_drafts.TryGetValue(documentId, out DraftEntry? draft)) m_drafts.Add(documentId, draft = new());
            ShaderEntry entry = draft.compilation;
            string targetVariant = target.key + "\n" + variant.value;
            if (!draft.attempted || draft.revision != revision || draft.assetRevision != m_assets.revision
                || entry.extensionGeneration != m_types.current.version || draft.targetVariant != targetVariant)
            {
                draft.attempted = true;
                draft.revision = revision;
                draft.assetRevision = m_assets.revision;
                bool environmentChanged = entry.extensionGeneration != m_types.current.version || draft.targetVariant != targetVariant;
                entry.extensionGeneration = m_types.current.version;
                draft.targetVariant = targetVariant;
                try
                {
                    byte[] captured = ShaderGraphArtifact.Capture(graph, m_types, m_serialization, AssetSerializationContext.Create(m_assets),
                        (id, _) =>
                        {
                            if (!m_assets.TryGetInfo(id, out AssetInfo? source) || source is null || source.status != AssetImportStatus.Imported)
                                throw new InvalidDataException($"Shader function '{id}' has no current successful import. Preview cannot hide its source error with an old bundle.");
                            using ArtifactLease lease = m_assets.AcquireArtifact(id, ShaderSourceBundle.outputName);
                            return File.ReadAllBytes(lease.info.absolutePath);
                        }, m_lifetime.Token);
                    string hash = ShaderGraphArtifact.GetSemanticHash(captured, m_serialization);
                    if (environmentChanged || entry.semanticHash != hash)
                    {
                        Retire(entry.pending, entry.cancellation);
                        entry.pending = null;
                        entry.cancellation = CancellationTokenSource.CreateLinkedTokenSource(m_lifetime.Token);
                        entry.semanticHash = hash;
                        entry.pending = RunOwned(token => m_shaderCompiler.CompileGraphAsync(captured, target, variant, m_types,
                            m_serialization, AssetSerializationContext.Create(m_assets), token), entry.cancellation.Token);
                    }
                }
                catch (Exception failure) when (Inno.Core.Execution.RetirementPendingException.Find(failure) is null)
                {
                    Retire(entry.pending, entry.cancellation);
                    entry.pending = null; entry.cancellation = null; entry.semanticHash = "";
                    entry.latestSucceeded = false;
                    entry.sourceDiagnostics = [new("SHADER_PREVIEW_CAPTURE", DiagnosticSeverity.Error, failure.Message)];
                }
            }
            if (entry.pending is { IsCompleted: true } pending)
            {
                if (pending.Exception is Exception failure && Inno.Core.Execution.RetirementPendingException.Find(failure) is not null) throw failure;
                entry.pending = null;
                entry.cancellation?.Dispose(); entry.cancellation = null;
                ShaderCompilationResult result = pending.IsCompletedSuccessfully ? pending.Result : new(null,
                    [new("SHADER_PREVIEW_COMPILE", DiagnosticSeverity.Error, pending.Exception?.GetBaseException().Message ?? "Preview compilation cancelled.")]);
                entry.latestSucceeded = result.succeeded;
                entry.sourceDiagnostics = Array.AsReadOnly(result.diagnostics.ToArray());
                if (result.succeeded) entry.artifact = result.artifact!.CreateRuntimeArtifact();
            }
            return new(entry.pending is not null ? EditorShaderCompilationState.Compiling
                : entry.latestSucceeded ? EditorShaderCompilationState.Succeeded : EditorShaderCompilationState.Failed,
                entry.artifact is not null && (entry.pending is not null || !entry.latestSucceeded), entry.sourceDiagnostics, entry.artifact);
        }
    }

    internal void ReleaseDraft(Guid documentId)
    {
        lock (m_sync)
        {
            EnsureActive();
            if (!m_drafts.Remove(documentId, out DraftEntry? draft)) return;
            Retire(draft.compilation.pending, draft.compilation.cancellation);
        }
    }

    private sealed class DraftEntry
    {
        internal bool attempted;
        internal ulong revision;
        internal long assetRevision;
        internal string targetVariant = "";
        internal readonly ShaderEntry compilation = new();
    }
}
