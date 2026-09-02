using System;
using System.IO;

using IOFile = System.IO.File;

namespace Inno.Assets.Pipeline;

internal static class AssetFileTransaction
{
    internal static void WriteAtomic(string targetPath, ReadOnlySpan<byte> bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(targetPath)!);
        string temporaryPath = targetPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            IOFile.WriteAllBytes(temporaryPath, bytes);
            IOFile.Move(temporaryPath, targetPath, overwrite: true);
        }
        finally
        {
            if (IOFile.Exists(temporaryPath))
                IOFile.Delete(temporaryPath);
        }
    }
}
