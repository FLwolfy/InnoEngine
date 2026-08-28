using System;
using Inno.Rendering.Core;
using Xunit;

namespace Inno.Rendering.Core.Tests;

public sealed class PipelineResourceTests
{
    [Fact]
    public void VertexLayout_RejectsDuplicateSemantic()
    {
        Assert.Throws<ArgumentException>(() => new RenderVertexLayout(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float3),
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float2)
        ]));
    }

    [Fact]
    public void PersistentVertexBuffer_RequiresMatchingLayout()
    {
        RenderBufferDescriptor buffer = new(3, 12, RenderBufferUsage.Vertex);
        RenderVertexLayout mismatched = new(
        [
            new RenderVertexAttribute(RenderVertexSemantic.Position, RenderVertexFormat.Float4)
        ]);

        Assert.Throws<ArgumentException>(() => new PersistentBufferDescriptor(buffer, mismatched));
    }

    [Fact]
    public void ShaderBinding_RejectsDefaultIdentifier()
    {
        Assert.Throws<ArgumentException>(() => new RenderShaderBindingDescriptor(
            default,
            RenderShaderBindingKind.Uniform));
    }
}
