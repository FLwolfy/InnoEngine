using System;
using Inno.Core.Math;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class Vector4PropertyRenderer :  PropertyRenderer<Vector4>
{
    protected override void Bind(string name, Func<Vector4> getter, Action<Vector4> setter, bool enabled)
    {
        Vector4 value = getter.Invoke();
        if (EditorGUILayout.Vector4Field(name, ref value, enabled))
        {
            setter.Invoke(value);
        }
    }
}