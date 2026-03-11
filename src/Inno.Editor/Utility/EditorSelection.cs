using ISerializable = Inno.Core.Serialization.ISerializable;

namespace Inno.Editor.Utility;

public class EditorSelection
{
    public ISerializable? selectedObject { get; private set; }
    public delegate void SelectionChangedHandler(ISerializable? oldObj, ISerializable? newObj);
    public event SelectionChangedHandler? OnSelectionChanged;

    public void Select(ISerializable obj)
    {
        if (selectedObject != obj)
        {
            var old = selectedObject;
            selectedObject = obj;
            OnSelectionChanged?.Invoke(old, obj);
        }
    }

    public void Deselect()
    {
        if (selectedObject != null)
        {
            var old = selectedObject;
            selectedObject = null;
            OnSelectionChanged?.Invoke(old, null);
        }
    }

    public bool IsSelected(object obj)
    {
        return selectedObject == obj;
    }
}

