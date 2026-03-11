using System.Collections.Generic;
using Inno.Core.Events;

namespace Inno.Core.Layers;

public class LayerStack
{
    private readonly List<Layer> m_layers = [];
    private int m_layerInsertIndex = 0;

    public void PushLayer(Layer layer)
    {
        m_layers.Insert(m_layerInsertIndex, layer);
        m_layerInsertIndex++;
        layer.OnAttach();
    }

    public void PushOverlay(Layer overlay)
    {
        m_layers.Add(overlay);
        overlay.OnAttach();
    }

    public void PopLayer(Layer layer)
    {
        if (m_layers.Remove(layer))
        {
            m_layerInsertIndex--;
            layer.OnDetach();
        }
    }

    public void PopOverlay(Layer overlay)
    {
        if (m_layers.Remove(overlay))
        {
            overlay.OnDetach();
        }
    }

    public void OnUpdate()
    {
        foreach (var layer in m_layers)
        {
            layer.OnUpdate();
        }
    }  
    
    public void OnRender()
    {
        foreach (var layer in m_layers)
        {
            layer.OnRender();
        }
    }

    public void OnEvent(Event e)
    {
        for (int i = m_layers.Count - 1; i >= 0; i--)
        {
            m_layers[i].OnEvent(e);
        }
    }
}