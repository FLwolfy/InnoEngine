using System;
using Inno.Core.Mathematics;

namespace Inno.Audio;

internal static class AudioValueValidation
{
    internal static void RequireFinite(Vector3 value, string parameterName)
    {
        if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z))
            throw new ArgumentOutOfRangeException(parameterName, "Audio vectors must contain finite components.");
    }
}
