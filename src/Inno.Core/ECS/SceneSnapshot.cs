using System.Collections.Generic;

using Inno.Core.Serialization;

namespace Inno.Core.ECS;

internal class SceneSnapshot : ISerializable
{
    [SerializableProperty] public List<GameObjectEntry> gameObjectEntries { get; private set; }

    private SceneSnapshot(List<GameObjectEntry> gameObjectEntries)
    {
        this.gameObjectEntries = gameObjectEntries;
    }
    
    public readonly struct ComponentEntry : ISerializable
    {
        [SerializableProperty] public string typeName { get; init; }
        [SerializableProperty] public SerializingState componentState { get; init; }
    }
    
    public readonly struct GameObjectEntry : ISerializable
    {
        [SerializableProperty] public SerializingState objectState { get; init; }
        [SerializableProperty] public List<ComponentEntry> componentEntries { get; init; }
    }

    public static SceneSnapshot Create(
        in List<GameObject> gameObjects,
        in ComponentPool componentPool)
    {
        List<GameObjectEntry> gameObjectEntries = new List<GameObjectEntry>();
        foreach (var gameObject in gameObjects)
        {
            List<ComponentEntry> componentEntries = new List<ComponentEntry>();
            foreach (var component in componentPool.GetAll(gameObject.id))
            {
                var type = component.GetType();
                componentEntries.Add(new ComponentEntry
                {
                    typeName = type.AssemblyQualifiedName ?? type.FullName ?? type.Name,
                    componentState = ((ISerializable)component).CaptureState()
                });
            }
            
            gameObjectEntries.Add(new GameObjectEntry
            {
                objectState = ((ISerializable)gameObject).CaptureState(),
                componentEntries = componentEntries
            });
        }
        
        return new SceneSnapshot(gameObjectEntries);
    }
}
