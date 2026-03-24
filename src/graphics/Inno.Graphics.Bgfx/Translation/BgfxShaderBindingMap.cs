using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxShaderBindingMap
{
    private readonly Dictionary<string, int> m_slotByName = new(StringComparer.Ordinal);

    public void Set(string name, int slot)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Binding name cannot be empty.", nameof(name));
        }

        m_slotByName[name] = slot;
    }

    public bool TryGetSlot(string name, out int slot)
    {
        return m_slotByName.TryGetValue(name, out slot);
    }

    public void Clear()
    {
        m_slotByName.Clear();
    }
}

