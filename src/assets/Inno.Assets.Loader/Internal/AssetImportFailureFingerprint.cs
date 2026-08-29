namespace Inno.Assets.Loader;

internal readonly record struct AssetImportFailureFingerprint(
    string relativePath,
    int status,
    string sourceHash,
    string importerId,
    string diagnostics);
