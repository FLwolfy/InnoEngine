using System;

namespace Inno.Platform.Graphics;

public interface IVertexBuffer : IDisposable
{
    void Set<T>(T[] data) where T : unmanaged;
}