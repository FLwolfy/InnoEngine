using System;
using System.Collections.Generic;
using System.Linq;

using Inno.Core.Storage;

namespace Inno.Engine.Scene;

/// <summary>Provides the non-generic contract for one concrete component bucket.</summary>
internal interface IComponentBucket
{
    Type componentType { get; }
    int count { get; }

    void Add(GameComponent component);
    bool Remove(GameComponent component);
    IReadOnlyList<GameComponent> GetSnapshot();
    void Clear();
}

internal sealed class ComponentBucket<TComponent> : IComponentBucket where TComponent : GameComponent
{
    private readonly ObjectPool<TComponent> m_components = new();

    public Type componentType => typeof(TComponent);
    public int count => m_components.count;

    public void Add(GameComponent component)
        => m_components.Add((TComponent)component);

    public bool Remove(GameComponent component)
        => component is TComponent typed && m_components.Remove(typed);

    public IReadOnlyList<GameComponent> GetSnapshot()
        => m_components.All().Cast<GameComponent>().ToArray();

    public void Clear() => m_components.RemoveAll();
}
