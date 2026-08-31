using Xunit;

namespace Inno.Core.Reflection.Tests;

[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class TypeCacheCollection
{
    public const string NAME = "TypeCacheCollection";
}
