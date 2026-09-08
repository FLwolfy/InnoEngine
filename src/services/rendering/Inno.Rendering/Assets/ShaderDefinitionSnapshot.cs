using System;
using System.Linq;

namespace Inno.Rendering;

internal sealed class ShaderDefinitionSnapshot
{
    private readonly ShaderDefinition m_definition;

    internal ShaderDefinitionSnapshot(ShaderDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (definition.properties is null || definition.keywords is null ||
            definition.passes is null || definition.techniques is null)
        {
            throw new ArgumentException("Shader declaration collections cannot be null.", nameof(definition));
        }
        m_definition = Copy(definition);
    }

    internal ShaderDefinition CreateDefinition() => Copy(m_definition);

    internal static ShaderPassDefinition Copy(ShaderPassDefinition definition)
    {
        definition.metadata = definition.metadata?.ToArray() ?? [];
        return definition;
    }

    internal static ShaderTechniqueDefinition Copy(ShaderTechniqueDefinition definition)
    {
        definition.passes = definition.passes?.ToArray() ?? [];
        return definition;
    }

    private static ShaderDefinition Copy(ShaderDefinition definition)
        => new()
        {
            name = definition.name,
            properties = definition.properties.ToArray(),
            keywords = definition.keywords.Select(static keyword => new ShaderKeywordDefinition
            {
                id = keyword.id,
                options = keyword.options?.ToArray() ?? []
            }).ToArray(),
            passes = definition.passes.Select(Copy).ToArray(),
            techniques = definition.techniques.Select(Copy).ToArray()
        };
}
