using System;

namespace Inno.Core.ECS;

public sealed class Entity(Guid id, Guid? parentGuid = null)
{
    public Guid id { get; } = id;

    public Guid? parentGuid { get; } = parentGuid;
}
