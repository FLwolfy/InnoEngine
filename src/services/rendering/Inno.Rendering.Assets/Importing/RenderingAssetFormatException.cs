using System;

namespace Inno.Rendering.Assets;

internal sealed class RenderingAssetFormatException : FormatException
{
    internal RenderingAssetFormatException(string path, string message)
        : base($"{path}: {message}")
    {
    }
}
