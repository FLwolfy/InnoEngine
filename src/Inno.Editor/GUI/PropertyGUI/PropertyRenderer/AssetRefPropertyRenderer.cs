using System;

using Inno.Assets.AssetType;
using Inno.Assets.Core;
using Inno.Core.Logging;
using Inno.Editor.Core;

namespace Inno.Editor.GUI.PropertyGUI.PropertyRenderer;

public class AssetRefPropertyRenderer<T> :  PropertyRenderer<AssetRef<T>> where T : InnoAsset
{
    protected override void Bind(string name, Func<AssetRef<T>> getter, Action<AssetRef<T>> setter, bool enabled)
    {
        AssetRef<T> assetRef = getter();
        string? assetName = assetRef.Resolve()?.name;
        string displayText = assetName ?? $"Drop {typeof(T).Name} Reference...";
        
        if (EditorGUILayout.DropRef(
                name, 
                displayText,
                EditorPayloadType.ASSET_REF_PAYLOAD, 
                ref assetRef,
                p => p.Resolve() != null,
                enabled))
        {
            setter.Invoke(assetRef);
        }
        
    }
}