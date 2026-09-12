using System;
using System.Collections.Generic;
using Inno.Core.Graphs;

namespace Inno.Rendering.Shaders;

internal static class StorageNodePorts
{
    internal static ShaderSourceType Resource(ShaderNodeDescriptionContext context)
        => context.Read("resource", new ShaderGraphType { isStorage = true, storageElement = new() { id = "float4" }, access = RenderStorageAccess.ReadWrite }).CreateType();
    internal static ShaderSourceType Coordinate(ShaderStorageType storage)
        => ShaderSourceType.Atomic(!storage.isImage ? "uint" : storage.array || storage.dimension == RenderTextureDimension.Texture3D ? "int3" : "int2");
    internal static IReadOnlyList<ShaderNodePort> Describe(ShaderNodeDescriptionContext context, bool write, bool atomic)
    {
        ShaderSourceType type = Resource(context);
        ShaderStorageType storage = type.storage ?? throw new ArgumentException("A storage node requires a storage resource descriptor.");
        var ports = new List<ShaderNodePort>
        {
            new("resource", type, GraphPortDirection.Input),
            new("coordinate", Coordinate(storage), GraphPortDirection.Input),
            new("after", ShaderSourceType.Atomic("bool"), GraphPortDirection.Input, false),
            new("then", ShaderSourceType.Atomic("bool"), GraphPortDirection.Output)
        };
        if (write) ports.Add(new("value", storage.valueType, GraphPortDirection.Input));
        if (!write || atomic) ports.Add(new(atomic ? "previous" : "value", storage.valueType, GraphPortDirection.Output));
        return ports;
    }
}

[ShaderNodeCompilerExtension]
internal sealed class ShaderStorageLoadNodeCompiler : IShaderNodeCompiler
{
    public string definitionId => "inno.shader.storage-load";
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context) => StorageNodePorts.Describe(context, false, false);
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue>
        {
            ["value"] = context.builder.LoadStorage(context.Input("resource"), context.Input("coordinate")),
            ["then"] = context.builder.Constant(true)
        };
}

[ShaderNodeCompilerExtension]
internal sealed class ShaderStorageStoreNodeCompiler : IShaderNodeCompiler
{
    public string definitionId => "inno.shader.storage-store";
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context) => StorageNodePorts.Describe(context, true, false);
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
    {
        context.builder.StoreStorage(context.Input("resource"), context.Input("coordinate"), context.Input("value"));
        return new Dictionary<string, ShaderIrValue> { ["then"] = context.builder.Constant(true) };
    }
}

[ShaderNodeCompilerExtension]
internal sealed class ShaderStorageAtomicAddNodeCompiler : IShaderNodeCompiler
{
    public string definitionId => "inno.shader.storage-atomic-add";
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context) => StorageNodePorts.Describe(context, true, true);
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
        => new Dictionary<string, ShaderIrValue>
        {
            ["previous"] = context.builder.AtomicAddStorage(context.Input("resource"), context.Input("coordinate"), context.Input("value")),
            ["then"] = context.builder.Constant(true)
        };
}

[ShaderNodeCompilerExtension]
internal sealed class ShaderDiscardNodeCompiler : IShaderNodeCompiler
{
    public string definitionId => "inno.shader.discard";
    public IReadOnlyList<ShaderNodePort> GetPorts(ShaderNodeDescriptionContext context)
        => [new("condition", ShaderSourceType.Atomic("bool"), GraphPortDirection.Input),
            new("after", ShaderSourceType.Atomic("bool"), GraphPortDirection.Input, false),
            new("then", ShaderSourceType.Atomic("bool"), GraphPortDirection.Output)];
    public IReadOnlyDictionary<string, ShaderIrValue> Lower(ShaderNodeLoweringContext context)
    {
        context.builder.Discard(context.Input("condition"));
        return new Dictionary<string, ShaderIrValue> { ["then"] = context.builder.Constant(true) };
    }
}
