# AssetManager API Examples

## Initialize

```csharp
AssetManager.Initialize(new AssetManagerOptions
{
    assetRoot = "Assets",
    artifactRoot = "Library/Artifacts",
    autoRegisterBuiltInImporters = true,
    autoRegisterImportersFromTypeCache = true
});
```

## Load / Handle / Resolve

```csharp
var text = AssetManager.Load<TextAsset>("Config/game.json");

AssetHandle<TextAsset> handle = AssetManager.GetHandle<TextAsset>("Config/game.json");
if (AssetManager.TryResolve(handle, out TextAsset loaded))
{
    Console.WriteLine(loaded.content);
}
```

## Save

```csharp
TextAsset cfg = AssetManager.Load<TextAsset>("Config/game.json");
AssetManager.Save(cfg);
```

## Reimport

```csharp
var shader = AssetManager.Reimport<ShaderAsset>("Shaders/lit.frag");
```

## Custom Importer Example

```csharp
public sealed class IniImporter : AssetImporter<TextAsset>
{
    public override IReadOnlyList<string> supportedExtensions { get; } = [".ini"];

    public override AssetImportResult<TextAsset> ImportTyped(in AssetImportContext context)
    {
        string content = context.ReadUtf8Text();
        return new AssetImportResult<TextAsset>(new TextAsset(content, "ini"), context.sourceBytes.ToArray());
    }

    public override bool TryExportTyped(TextAsset asset, out byte[] sourceBytes)
    {
        sourceBytes = Encoding.UTF8.GetBytes(asset.content);
        return true;
    }
}
```

```csharp
AssetManager.RegisterImporter<IniImporter>();
```

## Built-in Basic Types

- `TextAsset`
- `BinaryAsset`
- `ShaderAsset`
- `TextureAsset`
