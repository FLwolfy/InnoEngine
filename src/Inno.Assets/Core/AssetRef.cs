using System;

using Inno.Assets.AssetType;

namespace Inno.Assets.Core;

public struct AssetRef<T> where T : InnoAsset
{
    public Guid guid { get; private set; } = Guid.Empty;
    public bool isEmbedded { get; private set; }

    internal AssetRef(Guid guid, bool isEmbedded)
    {
        this.guid = guid; 
        this.isEmbedded = isEmbedded;
    }

    public bool isValid => guid != Guid.Empty;

    public T? Resolve() => AssetManager.ResolveAssetRef(this);

    public override string ToString()
    {
        if (!isValid) 
            return $"{typeof(T).Name}: Invalid";
        
        if (isEmbedded) 
            return $"{typeof(T).Name}: (Embedded) {guid}";
        
        return $"{typeof(T).Name}: {guid}";
    }
}