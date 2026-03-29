namespace Inno.Core.ECS;

public interface ISystem
{
    int order { get; }

    void Update(World world, float deltaTime);
}

public abstract class System<TComponent> : ISystem
    where TComponent : Component
{
    public virtual int order => 0;

    protected abstract void Process(World world, Entity entity, TComponent component, float deltaTime);

    public void Update(World world, float deltaTime)
    {
        foreach ((Entity entity, TComponent component) in world.QueryTyped<TComponent>())
        {
            if (!component.enabled)
            {
                continue;
            }

            Process(world, entity, component, deltaTime);
        }
    }
}
