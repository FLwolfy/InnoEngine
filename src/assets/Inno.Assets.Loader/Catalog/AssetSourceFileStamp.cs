using System;
using System.IO;

namespace Inno.Assets.Loader;

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

    public bool Equals(AssetSourceFileStamp other)
        => isValid == other.isValid &&
           length == other.length &&
           lastWriteUtcTicks == other.lastWriteUtcTicks &&
           creationTimeUtcTicks == other.creationTimeUtcTicks;

    public override bool Equals(object? obj)
        => obj is AssetSourceFileStamp other && Equals(other);

    public override int GetHashCode()
        => HashCode.Combine(isValid, length, lastWriteUtcTicks, creationTimeUtcTicks);

    public static bool operator ==(AssetSourceFileStamp left, AssetSourceFileStamp right)
        => left.Equals(right);

    public static bool operator !=(AssetSourceFileStamp left, AssetSourceFileStamp right)
        => !left.Equals(right);
}
