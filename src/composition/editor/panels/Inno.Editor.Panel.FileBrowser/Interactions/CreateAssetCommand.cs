using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Inno.Assets;
using Inno.Assets.Pipeline;
using Inno.Editor.Assets;
using Inno.Editor.Interactions;

namespace Inno.Editor.Panel.FileBrowser;

[EditorAction(FileBrowserInteractionIds.C_CREATE_ASSET, FileBrowserInteractionIds.C_AREA)]
internal sealed class CreateAssetCommand(AssetEditorModule assets) : EditorAction<string, string>
{
    protected override EditorActionState Query(EditorActionContext<string, string> context)
        => assets.CanCreateAsset(context.target, context.argument)
            ? EditorActionState.enabled
            : EditorActionState.disabled;

    protected override void Execute(EditorActionContext<string, string> context)
    {
        AssetFileEntry created = assets.CreateAsset(context.target, context.argument);
        context.interactions.SetSelection(created);
        context.interactions.OpenPanel("scene.inspector");
    }
}

[EditorMenuSource(FileBrowserInteractionIds.C_AREA)]
internal sealed class AssetCreationMenu(AssetEditorModule assets) : EditorMenuSource
{
    public override void Build(EditorMenuContext context, EditorMenuBuilder builder)
    {
        IReadOnlyList<AssetCreationRegistry.Registration> templates = assets.creationTemplates;
        var declaredGroups = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AssetCreationRegistry.Registration registration in templates)
        {
            AssetCreationMenuAttribute declaration = registration.declaration;
            string[] segments = declaration.menuPath.Split('/');
            if (segments.Length > 1)
            {
                string topLevel = "Create/" + segments[0];
                if (declaredGroups.Add(topLevel))
                {
                    builder.AddGroup(
                        topLevel,
                        declaration.groupOrder,
                        declaration.separatorBeforeGroup);
                }
                for (int depth = 1; depth < segments.Length - 1; depth++)
                {
                    string group = "Create/" + string.Join('/', segments.Take(depth + 1));
                    if (declaredGroups.Add(group))
                        builder.AddGroup(group, declaration.itemOrder);
                }
            }

            builder.Add(
                "Create/" + declaration.menuPath,
                FileBrowserInteractionIds.C_CREATE_ASSET,
                declaration.itemOrder,
                argument: declaration.id);
        }
    }
}
