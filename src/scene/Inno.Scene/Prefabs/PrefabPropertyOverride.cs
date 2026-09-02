using System;

namespace Inno.Scene;

internal sealed record PrefabPropertyOverride(
    Guid sourceComponentId,
    string propertyName,
    byte[] value,
    bool isOrphaned = false);
