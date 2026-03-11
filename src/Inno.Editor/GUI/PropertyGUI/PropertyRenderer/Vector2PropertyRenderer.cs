using System;
using Inno.Core.Math;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class Vector2PropertyRenderer :  PropertyRenderer<Vector2>
{
    protected override void Bind(string name, Func<Vector2> getter, Action<Vector2> setter, bool enabled)
    {
        Vector2 value = getter.Invoke();
        if (EditorGUILayout.Vector2Field(name, ref value, enabled))
        {
            setter.Invoke(value);
        }
    }
}