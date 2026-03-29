using Xunit;

namespace Inno.Core.Reflection.Tests;

[CollectionDefinition(NAME, DisableParallelization = true)]
public sealed class TypeIdentityRegistryCollection
{
    public const string NAME = "TypeIdentityRegistryCollection";
}
