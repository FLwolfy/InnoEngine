using Inno.Graphics;
using Inno.Native.Bgfx;

namespace Inno.Graphics.Bgfx;

public sealed class BgfxViewAllocator
{
    private ushort m_nextId = 1;

    public ushort Allocate()
    {
        var id = m_nextId;
        m_nextId++;
        return id;
    }
}
