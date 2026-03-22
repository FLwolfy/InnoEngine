using System;

namespace Inno.Platform.Graphics;

public interface IIndexBuffer : IDisposable
{
    void Set<T>(T[] data) where T : unmanaged;
}