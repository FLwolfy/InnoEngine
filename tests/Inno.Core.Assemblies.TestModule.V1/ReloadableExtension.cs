using Inno.Core.Reflection;
using Reloadable.PrivateDependency;

namespace Inno.Core.Assemblies.TestModule;

[StableTypeId("44a4cda2-a03e-4918-8db2-f37048a9e4f1")]
public sealed class ReloadableExtension
{
    public int version => VersionSource.baseVersion;
}
