using Inno.Core.ECS;
using Inno.Core.Identity;

namespace Inno.Engine.Scene;

public class GameScene : IIdentityObject
{
    private World m_world = new World();
}
