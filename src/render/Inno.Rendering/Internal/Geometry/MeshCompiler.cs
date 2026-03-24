
namespace Inno.Rendering;

internal sealed class MeshCompiler
{
    public CompiledMesh Compile(Mesh mesh)
    {
        return new CompiledMesh
        {
            source = mesh
        };
    }
}
