using Xunit;

namespace Inno.Extensibility.Types.Tests;

[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class TypeCacheCollection
{
    public const string NAME = "TypeCacheCollection";
}
