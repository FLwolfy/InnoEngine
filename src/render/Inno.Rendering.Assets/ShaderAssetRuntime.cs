using System;

namespace Inno.Rendering.Assets;

/// <summary>
/// Reads backend-neutral shader IR committed by the rendering asset importer.
/// </summary>
public static class ShaderAssetRuntime
{
    /// <summary>
    /// Decodes the current committed IR payload.
    /// </summary>
    /// <param name="shader">Imported shader asset.</param>
    /// <returns>The shared handwritten/graph shader IR.</returns>
    /// <exception cref="InvalidOperationException">Thrown when the asset has no committed IR payload.</exception>
    public static ShaderIRModule GetModule(ShaderAsset shader)
    {
        ArgumentNullException.ThrowIfNull(shader);
        if (shader.runtimePayload.IsEmpty)
        {
            throw new InvalidOperationException($"Shader asset '{shader.name}' has no committed IR payload.");
        }

        return ShaderIRArtifactSerialization.Decode(shader.runtimePayload.Span);
    }
}
