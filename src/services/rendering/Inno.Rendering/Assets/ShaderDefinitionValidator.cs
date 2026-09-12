using System;
using System.Collections.Generic;
using System.Linq;
using Inno.Core.Diagnostics;

namespace Inno.Rendering;

/// <summary>Validates the source-free material, pass and technique contract shared by graph compilation and deployed shaders.</summary>
public static class ShaderDefinitionValidator
{
    /// <summary>Checks declaration identities, resource kinds, stage visibility and open technique mappings without mutating the candidate.</summary>
    /// <param name="definition">The candidate runtime contract.</param>
    /// <param name="capabilities">Optional device capabilities; unsupported optional passes produce warnings.</param>
    /// <returns>Immutable diagnostics; an empty collection indicates a valid declaration contract.</returns>
    /// <exception cref="ArgumentNullException">The definition is null.</exception>
    public static IReadOnlyList<ShaderDiagnostic> Validate(ShaderDefinition definition, GraphicsCapabilities? capabilities = null)
    {
        ArgumentNullException.ThrowIfNull(definition);
        var diagnostics = new List<ShaderDiagnostic>();
        if (string.IsNullOrWhiteSpace(definition.name)) Error("SHADER_NAME_MISSING", "A shader requires a nonempty name.");
        if (definition.properties is null || definition.passes is null || definition.keywords is null || definition.techniques is null)
        {
            Error("SHADER_DECLARATIONS_MISSING", "Shader declaration collections cannot be null.");
            return diagnostics.AsReadOnly();
        }
        Identifiers(definition.properties.Select(static value => value.id.value), "PROPERTY");
        Identifiers(definition.passes.Select(static value => value.name), "PASS");
        Identifiers(definition.keywords.Select(static value => value.id), "KEYWORD");
        Identifiers(definition.techniques.Select(static value => value.id.value), "TECHNIQUE");
        if (definition.passes.Length == 0) Error("SHADER_PASSES_MISSING", "A shader requires at least one pass.");
        foreach (ShaderPropertyDefinition property in definition.properties)
        {
            if (!Enum.IsDefined(property.type) || !ShaderPropertyDefinition.IsBindingKindCompatible(property.type, property.bindingKind))
                Error("SHADER_PROPERTY_BINDING_INCOMPATIBLE", $"Property '{property.id}' has incompatible type '{property.type}' and binding '{property.bindingKind}'.");
            if (!Enum.IsDefined(property.storageAccess)) Error("SHADER_STORAGE_ACCESS_INVALID", $"Property '{property.id}' has invalid storage access.");
            if (!Enum.IsDefined(property.bindingOwner)) Error("SHADER_BINDING_OWNER_INVALID", $"Property '{property.id}' has invalid binding ownership.");
            if (property.stages == ShaderStage.None || (property.stages & ~(ShaderStage.Vertex | ShaderStage.Fragment | ShaderStage.Compute)) != 0)
                Error("SHADER_PROPERTY_STAGES_INVALID", $"Property '{property.id}' requires valid stage visibility.");
        }
        foreach (ShaderKeywordDefinition keyword in definition.keywords)
        {
            if (keyword.options is null || keyword.options.Length == 0) Error("SHADER_KEYWORD_OPTIONS_MISSING", $"Keyword '{keyword.id}' requires selectable options.");
            else Identifiers(keyword.options, "KEYWORD_OPTION");
        }
        HashSet<string> passes = definition.passes.Select(static value => value.name).ToHashSet(StringComparer.Ordinal);
        foreach (ShaderTechniqueDefinition technique in definition.techniques)
        {
            if (string.IsNullOrWhiteSpace(technique.contract.value)) Error("SHADER_TECHNIQUE_CONTRACT_MISSING", $"Technique '{technique.id}' requires a contract ID.");
            if (technique.passes is null) { Error("SHADER_TECHNIQUE_ROLES_MISSING", $"Technique '{technique.id}' requires a role collection."); continue; }
            Identifiers(technique.passes.Select(static value => value.role.value), "TECHNIQUE_ROLE");
            foreach (ShaderTechniquePass binding in technique.passes)
                if (!passes.Contains(binding.passName)) Error("SHADER_UNKNOWN_TECHNIQUE_PASS", $"Technique '{technique.id}' refers to absent pass '{binding.passName}'.");
        }
        foreach (ShaderPassDefinition pass in definition.passes)
        {
            if (!Enum.IsDefined(pass.programKind)) Error("SHADER_PASS_KIND_INVALID", $"Pass '{pass.name}' has invalid program kind.");
            if (!Enum.IsDefined(pass.renderState.cull) || !Enum.IsDefined(pass.renderState.depthCompare) || (pass.renderState.colorWriteMask & ~15) != 0)
                Error("SHADER_RASTER_STATE_INVALID", $"Pass '{pass.name}' has invalid raster state.");
            if (capabilities is not null && (pass.requiredFeatures & ~capabilities.features) != 0)
                diagnostics.Add(new("SHADER_CAPABILITY_UNAVAILABLE", DiagnosticSeverity.Warning, $"Pass '{pass.name}' requires unavailable features '{pass.requiredFeatures & ~capabilities.features}'."));
        }
        return diagnostics.AsReadOnly();

        void Error(string code, string message) => diagnostics.Add(new(code, DiagnosticSeverity.Error, message));
        void Identifiers(IEnumerable<string> values, string kind)
        {
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (string id in values)
                if (string.IsNullOrWhiteSpace(id)) Error("SHADER_" + kind + "_ID_MISSING", $"A {kind.ToLowerInvariant()} identity is empty.");
                else if (!seen.Add(id)) Error("SHADER_DUPLICATE_" + kind, $"The {kind.ToLowerInvariant()} identity '{id}' is repeated.");
        }
    }
}
