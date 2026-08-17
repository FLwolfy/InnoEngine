using System;

namespace Inno.Engine.Scene;

internal sealed record PrefabPropertyOverride(
    Guid sourceComponentId,
    string propertyName,
    byte[] value,
    bool isOrphaned = false);
