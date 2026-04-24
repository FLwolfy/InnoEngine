using System;
using System.Security.Cryptography;

namespace Inno.Assets.Core;

/// <summary>
/// Utility helpers for asset hashing.
/// </summary>
public static class AssetHashUtility
{
    /// <summary>
    /// Computes SHA-256 hex string (uppercase) for given bytes.
    /// </summary>
    /// <param name="data">Source data bytes.</param>
    /// <returns>Hex-encoded SHA-256.</returns>
    public static string ComputeSha256Hex(ReadOnlySpan<byte> data)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(data, hash);
        return Convert.ToHexString(hash);
    }
}
