using System;
using Inno.Assets;
using Inno.Core.Execution;

namespace Inno.Audio.Runtime;

internal sealed class AudioClipRequest : IDisposable
{
    private ArtifactLease? m_artifact;
    private readonly string? m_failure;

    internal AudioClipRequest(AudioClipAsset clip, IAssetArtifactLookup artifacts)
    {
        persistentId = clip.identity.persistentId;
        contentVersion = clip.contentVersion;
        assetPath = clip.assetPath;
        metadata = clip.metadata;
        try
        {
            if (clip.isMissing || metadata is null)
                throw new InvalidOperationException("The audio clip or its imported metadata is missing.");
            m_artifact = artifacts.AcquireArtifact(persistentId, "audio-data");
        }
        catch (Exception pendingRetirement) when (RetirementPendingException.Find(pendingRetirement) is not null) { throw; }
        catch (Exception exception)
        {
            m_failure = exception.Message;
        }
    }

    internal Guid persistentId { get; }
    internal long contentVersion { get; }
    internal AssetPath assetPath { get; }
    internal AudioClipMetadata? metadata { get; }
    internal AssetArtifactInfo artifact => m_artifact?.info
        ?? throw new InvalidOperationException(m_failure ?? "The clip artifact was transferred or released.");

    internal ArtifactLease TakeArtifact()
    {
        _ = artifact;
        ArtifactLease result = m_artifact!;
        m_artifact = null;
        return result;
    }

    /// <summary>
    /// Releases an artifact that was not transferred into the native clip cache.
    /// </summary>
    public void Dispose()
    {
        m_artifact?.Dispose();
        m_artifact = null;
    }
}
