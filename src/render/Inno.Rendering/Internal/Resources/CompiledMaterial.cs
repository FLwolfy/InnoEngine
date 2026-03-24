
namespace Inno.Rendering;

internal sealed class CompiledMaterial
{
    public required Material source { get; init; }

    public required ShaderPermutationKey permutationKey { get; init; }
}

