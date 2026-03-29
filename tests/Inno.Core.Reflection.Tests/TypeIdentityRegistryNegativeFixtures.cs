using Inno.Core.Reflection;

namespace TypeIdentityRegistryNegativeFixtures;

[StableTypeId("33333333-3333-3333-3333-333333333333")]
internal sealed class DuplicateStableTypeA;

[StableTypeId("33333333-3333-3333-3333-333333333333")]
internal sealed class DuplicateStableTypeB;

[StableTypeId("not-a-guid")]
internal sealed class InvalidStableType;
