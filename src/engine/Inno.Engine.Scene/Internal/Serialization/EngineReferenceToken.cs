using System;

namespace Inno.Engine.Scene;

internal enum EngineReferenceKind
{
    GameObject = 1,
    GameComponent = 2
}

internal readonly record struct EngineReferenceToken(
    EngineReferenceKind kind,
    Guid sourceId);
