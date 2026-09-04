using System;

namespace Inno.Scene;

[Flags]
internal enum PrefabObjectOverrideKind
{
    None = 0,
    Name = 1 << 0,
    ActiveSelf = 1 << 1,
    Parent = 1 << 2,
    SiblingIndex = 1 << 3,
    Tag = 1 << 4,
}

internal sealed record PrefabStructureOverride(
    Guid sourceObjectId,
    PrefabObjectOverrideKind kind,
    bool isOrphaned = false);
