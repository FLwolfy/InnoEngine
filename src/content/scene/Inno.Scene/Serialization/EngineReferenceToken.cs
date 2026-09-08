using System;

namespace Inno.Scene;

internal enum EngineReferenceKind
{
    GameObject = 1,
    GameComponent = 2,
    GameSystem = 3
}

internal readonly record struct EngineReferenceToken(
    EngineReferenceKind kind,
    Guid sourceId);
