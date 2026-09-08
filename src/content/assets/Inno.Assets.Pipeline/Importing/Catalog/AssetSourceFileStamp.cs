using System;
using System.IO;

namespace Inno.Assets.Pipeline;

internal readonly struct AssetSourceFileStamp : IEquatable<AssetSourceFileStamp>
{
    private AssetSourceFileStamp(
        long length,
        long lastWriteUtcTicks,
        long creationTimeUtcTicks)
    {
        this.length = length;
        this.lastWriteUtcTicks = lastWriteUtcTicks;
        this.creationTimeUtcTicks = creationTimeUtcTicks;
        isValid = true;
    }

    internal bool isValid { get; }
    internal long length { get; }
    internal long lastWriteUtcTicks { get; }
    internal long creationTimeUtcTicks { get; }

    internal static bool TryCapture(string path, out AssetSourceFileStamp stamp)
    {
        try
        {
            var info = new FileInfo(path);
            info.Refresh();
            if (!info.Exists)
            {
                stamp = default;
                return false;
            }

            stamp = new AssetSourceFileStamp(
                info.Length,
                info.LastWriteTimeUtc.Ticks,
                info.CreationTimeUtc.Ticks);
            return true;
        }
        catch (IOException)
        {
            stamp = default;
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            stamp = default;
            return false;
        }
    }

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="other">
    /// The strongly typed value compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public bool Equals(AssetSourceFileStamp other)
        => isValid == other.isValid &&
           length == other.length &&
           lastWriteUtcTicks == other.lastWriteUtcTicks &&
           creationTimeUtcTicks == other.creationTimeUtcTicks;

    /// <summary>
    /// Determines whether this value and the supplied value represent the same logical state.
    /// </summary>
    /// <param name="obj">
    /// The object compared with this value.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public override bool Equals(object? obj)
        => obj is AssetSourceFileStamp other && Equals(other);

    /// <summary>
    /// Computes a hash code consistent with the implemented equality contract.
    /// </summary>
    /// <returns>
    /// The scalar result calculated from the supplied inputs.
    /// </returns>
    public override int GetHashCode()
        => HashCode.Combine(isValid, length, lastWriteUtcTicks, creationTimeUtcTicks);

    /// <summary>
    /// Determines whether the supplied values are equal under the type's equality tolerance.
    /// </summary>
    /// <param name="left">
    /// The left consumed by ==; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="right">
    /// The right consumed by ==; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator ==(AssetSourceFileStamp left, AssetSourceFileStamp right)
        => left.Equals(right);

    /// <summary>
    /// Determines whether the supplied values differ under the type's equality tolerance.
    /// </summary>
    /// <param name="left">
    /// The left consumed by !=; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="right">
    /// The right consumed by !=; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the requested condition is satisfied; otherwise, <see langword="false"/>.
    /// </returns>
    public static bool operator !=(AssetSourceFileStamp left, AssetSourceFileStamp right)
        => !left.Equals(right);
}
