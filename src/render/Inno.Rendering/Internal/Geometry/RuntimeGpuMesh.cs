using Inno.Graphics;

namespace Inno.Rendering;

internal sealed class RuntimeGpuMesh : IDisposable
{
    public RuntimeGpuMesh(Mesh source, IGraphicsBuffer vertexBuffer, IGraphicsBuffer? indexBuffer, int vertexCount, int indexCount, IGraphicsInputLayout inputLayout)
    {
        this.source = source;
        this.vertexBuffer = vertexBuffer;
        this.indexBuffer = indexBuffer;
        this.vertexCount = vertexCount;
        this.indexCount = indexCount;
        this.inputLayout = inputLayout;
    }

    public Mesh source { get; }

    public IGraphicsBuffer vertexBuffer { get; }

    public IGraphicsBuffer? indexBuffer { get; }

    public int vertexCount { get; }

    public int indexCount { get; }

    public IGraphicsInputLayout inputLayout { get; }

    public void Dispose()
    {
        inputLayout.Dispose();
        indexBuffer?.Dispose();
        vertexBuffer.Dispose();
    }
}
