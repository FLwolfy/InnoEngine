using Inno.Assets.Yaml;

using Xunit;

namespace Inno.Assets.Yaml.Tests;

public sealed class LegacyYamlRemovedTests
{
    [Fact]
    public void LegacyYamlModule_IsIntentionallyEmptyMarker()
    {
        Assert.NotNull(typeof(LegacyYamlRemoved));
    }
}
