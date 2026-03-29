using System;

namespace Inno.Core.ECS;

public abstract class Component
{
    public Guid entityId { get; internal set; }

    public bool enabled { get; set; } = true;

    public virtual void Reset() { }
}
