using Inno.Core.Events;

namespace Inno.Core.Layers;

public abstract class Layer(string name = "Layer")
{
    public string name { get; } = name;

    public virtual void OnUpdate() { }
    public virtual void OnRender() { }
    public virtual void OnEvent(Event e) { }
    public virtual void OnAttach() { }
    public virtual void OnDetach() { }
}