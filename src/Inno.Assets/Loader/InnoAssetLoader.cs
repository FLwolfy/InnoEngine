using System;
using System.IO;
using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Assets.Serializer;
using Inno.Core.Serialization;

namespace Inno.Assets.Loader;

internal interface IAssetLoader
{
    /// <summary>
    /// Load the asset from disk / raw file.
    /// </summary>
    InnoAsset? Load(string path);
    
    /// <summary>
    /// Load the asset directly from bytes.
    /// </summary>
    InnoAsset LoadRaw(string assetName, Guid assetGuid, byte[] rawBytes);
    
    /// <summary>
    /// Save modifications back to the ORIGINAL source file, and sync meta/bin.
    /// relativePath follows the same convention as Load(): path under AssetManager.assetDirectory.
    /// </summary>
    void SaveSource(string relativePath, InnoAsset asset);
}

internal abstract class InnoAssetLoader<T> : IAssetLoader where T : InnoAsset
{
    private const string VIRTUAL_SOURCE_NAME = "InnoVirtualAsset";
    
    public abstract string[] validExtensions { get; }

    public InnoAsset? Load(string relativePath)
    {
        relativePath = relativePath.TrimEnd('/', '\\');

        string requestedAbsSourcePath = Path.Combine(AssetManager.assetDirectory, relativePath);

        string assetMetaPath = Path.Combine(
            AssetManager.assetDirectory,
            relativePath + AssetManager.C_ASSET_POSTFIX);

        string assetBinPath = Path.Combine(
            AssetManager.binDirectory,
            relativePath + AssetManager.C_BINARY_ASSET_POSTFIX);

        // -------------------- Import (no meta) --------------------
        if (!File.Exists(assetMetaPath))
        {
            if (!File.Exists(requestedAbsSourcePath))
                return null;

            byte[] raw = File.ReadAllBytes(requestedAbsSourcePath);

            string assetName = Path.GetFileName(relativePath);
            byte[] bin = OnLoadBinaries(assetName, raw, out T asset);

            asset.SetSourcePath(relativePath);
            asset.RecomputeHash(relativePath);

            WriteMeta(assetMetaPath, asset);
            WriteBin(assetBinPath, bin);

            asset.assetBinaries = bin;
            return asset;
        }

        // -------------------- Load meta --------------------
        var yaml = File.ReadAllText(assetMetaPath);
        var assetLoaded = AssetYamlSerializer.DeserializeAssetFromYaml<T>(yaml);

        // -------------------- Resolve source path --------------------
        string recordedRelSourcePath = string.IsNullOrWhiteSpace(assetLoaded.sourcePath)
            ? relativePath
            : assetLoaded.sourcePath;

        string recordedAbsSourcePath = Path.Combine(AssetManager.assetDirectory, recordedRelSourcePath);

        if (!File.Exists(recordedAbsSourcePath))
        {
            if (File.Exists(requestedAbsSourcePath))
            {
                recordedRelSourcePath = relativePath;
                recordedAbsSourcePath = requestedAbsSourcePath;
                assetLoaded.SetSourcePath(recordedRelSourcePath);
            }
            else
            {
                if (File.Exists(assetMetaPath)) File.Delete(assetMetaPath);
                if (File.Exists(assetBinPath)) File.Delete(assetBinPath);

                return null;
            }
        }

        // -------------------- Rebuild if source changed --------------------
        string oldHash = assetLoaded.sourceHash;

        assetLoaded.SetSourcePath(recordedRelSourcePath);
        assetLoaded.RecomputeHash(recordedRelSourcePath);

        if (oldHash != assetLoaded.sourceHash)
        {
            byte[] raw = File.ReadAllBytes(recordedAbsSourcePath);

            string assetName = Path.GetFileName(recordedRelSourcePath);
            byte[] bin = OnLoadBinaries(assetName, raw, out T rebuilt);

            rebuilt.guid = assetLoaded.guid;
            rebuilt.SetSourcePath(recordedRelSourcePath);
            rebuilt.RecomputeHash(recordedRelSourcePath);

            WriteMeta(assetMetaPath, rebuilt);
            WriteBin(assetBinPath, bin);

            rebuilt.assetBinaries = bin;
            return rebuilt;
        }

        // -------------------- Ensure bin exists --------------------
        if (!File.Exists(assetBinPath))
        {
            byte[] raw = File.ReadAllBytes(recordedAbsSourcePath);

            string assetName = Path.GetFileName(recordedRelSourcePath);
            byte[] bin = OnLoadBinaries(assetName, raw, out _);

            WriteBin(assetBinPath, bin);
        }

        // -------------------- Attach binaries --------------------
        byte[] data = File.ReadAllBytes(assetBinPath);
        assetLoaded.assetBinaries = data;
        return assetLoaded;
    }

    private static void WriteMeta(string metaPath, T asset)
    {
        string yaml = AssetYamlSerializer.SerializeAssetToYaml(asset);
        Directory.CreateDirectory(Path.GetDirectoryName(metaPath)!);
        File.WriteAllText(metaPath, yaml);
    }

    private static void WriteBin(string binPath, byte[] bin)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(binPath)!);
        File.WriteAllBytes(binPath, bin);
    }
    
    public InnoAsset LoadRaw(string assetName, Guid assetGuid, byte[] rawBytes)
    {
        byte[] bin = OnLoadBinaries(assetName, rawBytes, out T asset);

        asset.guid = assetGuid;
        asset.SetSourcePath(VIRTUAL_SOURCE_NAME + "/" + assetName);
        asset.RecomputeHash(rawBytes);
        asset.assetBinaries = bin;
        return asset;
    }
    
    public void SaveSource(string relativePath, InnoAsset asset)
    {
        relativePath = relativePath.TrimEnd('/', '\\');

        if (asset is not T typed)
            throw new ArgumentException(
                $"Asset type mismatch. Expected {typeof(T).Name}, got {asset.GetType().Name}.");

        // Resolve paths (same pattern as Load)
        string absSourcePath = Path.Combine(AssetManager.assetDirectory, relativePath);

        string assetMetaPath = Path.Combine(
            AssetManager.assetDirectory,
            relativePath + AssetManager.C_ASSET_POSTFIX);

        string assetBinPath = Path.Combine(
            AssetManager.binDirectory,
            relativePath + AssetManager.C_BINARY_ASSET_POSTFIX);

        // Disallow saving virtual assets back to disk source
        if (!string.IsNullOrWhiteSpace(typed.sourcePath) &&
            typed.sourcePath.StartsWith(VIRTUAL_SOURCE_NAME, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Cannot SaveSource() for a virtual asset.");
        }

        // Ensure directory exists for source file
        Directory.CreateDirectory(Path.GetDirectoryName(absSourcePath)!);

        // 1) Encode asset -> raw source bytes (asset-type specific)
        string assetName = Path.GetFileName(relativePath);
        byte[] raw = OnSaveSource(assetName, typed);

        // 2) Write back to the ORIGINAL source file
        File.WriteAllBytes(absSourcePath, raw);

        // 3) Rebuild runtime binaries deterministically from raw bytes
        byte[] bin = OnLoadBinaries(assetName, raw, out _);
        typed.SetSourcePath(relativePath);
        typed.RecomputeHash(raw);

        // 5) Persist meta + bin
        WriteMeta(assetMetaPath, typed);
        WriteBin(assetBinPath, bin);

        // 6) Update in-memory runtime payload
        typed.assetBinaries = bin;
    }

    /// <summary>
    /// Loads raw source data and produces a binary representation suitable for runtime use,
    /// while simultaneously creating and populating the corresponding asset instance.
    ///
    /// <para>
    /// This method is the single authoritative entry point for:
    /// <list type="bullet">
    ///   <item>Decoding or interpreting raw source bytes (e.g. image, audio, text, etc.)</item>
    ///   <item>Constructing a fully-initialized asset instance of type <typeparamref name="T"/></item>
    ///   <item>Producing the binary data that will be persisted as the asset's runtime payload</item>
    /// </list>
    /// </para>
    ///
    /// <para>
    /// The returned binary data does not have to be a "compiled" format.
    /// It only needs to be a deterministic, runtime-consumable representation derived
    /// from <paramref name="rawBytes"/>. The exact structure and semantics of the binary
    /// data are asset-type specific.
    /// </para>
    ///
    /// <para>
    /// The <paramref name="asset"/> output must be a newly created instance and must have
    /// all asset-specific metadata populated (for example: dimensions, counts, formats, etc.).
    /// Any data required at runtime that cannot be inferred from the binary payload alone
    /// should be stored on the asset instance itself and will be serialized into the asset
    /// metadata file.
    /// </para>
    ///
    /// <para>
    /// This method may be invoked during:
    /// <list type="bullet">
    ///   <item>First-time asset import</item>
    ///   <item>Asset rebuild due to source content changes</item>
    ///   <item>Binary regeneration when the runtime payload is missing</item>
    /// </list>
    /// Implementations must therefore be deterministic and side-effect free
    /// with respect to the input parameters.
    /// </para>
    /// </summary>
    /// <param name="assetName">
    /// The logical name of the asset, typically derived from the source path.
    /// This value is intended for identification, diagnostics, or asset-specific processing,
    /// and must not be used to resolve file system paths.
    /// </param>
    /// <param name="rawBytes">
    /// The raw source bytes loaded directly from the original asset file.
    /// These bytes represent the authoritative source data and should not be modified.
    /// </param>
    /// <param name="asset">
    /// Outputs a newly created and fully populated asset instance corresponding
    /// to the provided source data. The caller assumes ownership of this instance.
    /// </param>
    /// <returns>
    /// A byte array containing the runtime binary representation of the asset.
    /// This data will be written to the asset's binary storage and later reloaded
    /// for runtime use.
    /// </returns>
    protected abstract byte[] OnLoadBinaries(
        string assetName,
        byte[] rawBytes,
        out T asset
    );
    
    
    /// <summary>
    /// Encode the current asset state back into its SOURCE file format.
    /// For example:
    /// - TextAsset -> UTF8 bytes
    /// - ShaderAsset -> text bytes
    /// - TextureAsset -> PNG bytes (if you support encoding)
    /// </summary>
    protected abstract byte[] OnSaveSource(
        string assetName, 
        in T asset
    );
}
