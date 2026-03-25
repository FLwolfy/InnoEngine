using Inno.Core.Mathematics;

namespace Inno.Rendering;

/// <summary>
/// Stores strongly-typed material runtime overrides.
/// </summary>
public sealed class MaterialPropertyBlock
{
    private readonly Dictionary<string, object> m_values = new(StringComparer.Ordinal);

    public void SetFloat(string name, float value) => m_values[name] = value;

    public void SetInt(string name, int value) => m_values[name] = value;

    public void SetBool(string name, bool value) => m_values[name] = value;

    public void SetVector2(string name, Vector2 value) => m_values[name] = value;

    public void SetVector3(string name, Vector3 value) => m_values[name] = value;

    public void SetVector4(string name, Vector4 value) => m_values[name] = value;

    public void SetColor(string name, Color value) => m_values[name] = value;

    public void SetTexture(string name, Texture? value) => m_values[name] = value as object ?? NullTexture.VALUE;

    public bool TryGetFloat(string name, out float value) => TryGet(name, out value);

    public bool TryGetInt(string name, out int value) => TryGet(name, out value);

    public bool TryGetBool(string name, out bool value) => TryGet(name, out value);

    public bool TryGetVector2(string name, out Vector2 value) => TryGet(name, out value);

    public bool TryGetVector3(string name, out Vector3 value) => TryGet(name, out value);

    public bool TryGetVector4(string name, out Vector4 value) => TryGet(name, out value);

    public bool TryGetColor(string name, out Color value) => TryGet(name, out value);

    public bool TryGetTexture(string name, out Texture? value)
    {
        if (m_values.TryGetValue(name, out var raw))
        {
            if (ReferenceEquals(raw, NullTexture.VALUE))
            {
                value = null;
                return true;
            }

            if (raw is Texture texture)
            {
                value = texture;
                return true;
            }
        }

        value = null;
        return false;
    }

    public IEnumerable<KeyValuePair<string, object>> EnumerateProperties()
    {
        return m_values;
    }

    private static class NullTexture
    {
        public static readonly object VALUE = new();
    }

    private bool TryGet<T>(string name, out T value)
    {
        if (m_values.TryGetValue(name, out var raw) && raw is T casted)
        {
            value = casted;
            return true;
        }

        value = default!;
        return false;
    }
}
