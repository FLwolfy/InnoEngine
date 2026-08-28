using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Inno.Core.Graphs;
using Inno.Rendering.Core;

namespace Inno.Rendering.ShaderGraph;

/// <summary>
/// Contains either a shared Shader IR module or graph/node-mapped diagnostics.
/// </summary>
public sealed class ShaderGraphCompileResult
{
    /// <summary>Creates a shader graph compilation result.</summary>
    /// <param name="module">Generated shared Shader IR, or <see langword="null"/> after failure.</param>
    /// <param name="diagnostics">Structured graph and source diagnostics.</param>
    public ShaderGraphCompileResult(
        ShaderIRModule? module,
        IReadOnlyList<ShaderDiagnostic> diagnostics)
    {
        ArgumentNullException.ThrowIfNull(diagnostics);
        this.module = module;
        this.diagnostics = diagnostics;
    }

    /// <summary>Gets generated shared Shader IR, or <see langword="null"/> after failure.</summary>
    public ShaderIRModule? module { get; }

    /// <summary>Gets graph and source diagnostics.</summary>
    public IReadOnlyList<ShaderDiagnostic> diagnostics { get; }

    /// <summary>Gets whether a complete IR module was generated.</summary>
    public bool succeeded => module is not null
        && diagnostics.All(static value => value.severity != ShaderDiagnosticSeverity.Error);
}

/// <summary>
/// Compiles neutral graph records through generation-scoped shader nodes into the common Shader IR.
/// </summary>
public static class ShaderGraphCompiler
{
    private const int C_MAX_LOCAL_LIGHTS = 8;

    private const string C_VARYING_SOURCE = """
        vec3 a_position    : POSITION;
        vec3 a_normal      : NORMAL;
        vec4 a_tangent     : TANGENT;
        vec2 a_texcoord0   : TEXCOORD0;
        vec3 v_worldNormal : TEXCOORD0;
        vec2 v_texcoord0   : TEXCOORD1;
        vec3 v_worldPosition : TEXCOORD2;
        """;

    /// <summary>Compiles one imported shader graph asset into shared Shader IR.</summary>
    /// <param name="asset">Imported graph asset.</param>
    /// <param name="registry">Active built-in and script node registry snapshot.</param>
    /// <returns>Generated IR or node-mapped diagnostics.</returns>
    public static ShaderGraphCompileResult Compile(
        ShaderGraphAsset asset,
        ShaderNodeRegistry registry)
    {
        ArgumentNullException.ThrowIfNull(asset);
        ArgumentNullException.ThrowIfNull(registry);
        if (asset.document is null)
        {
            return new ShaderGraphCompileResult(
                null,
                [new ShaderDiagnostic(
                    "SHADER_GRAPH_DOCUMENT_MISSING",
                    ShaderDiagnosticSeverity.Error,
                    $"Shader graph '{asset.name}' has no committed document.")]);
        }

        ShaderGraphCompileResult result = Compile(
            string.IsNullOrWhiteSpace(asset.sourcePath) ? asset.name : asset.sourcePath,
            asset.name,
            asset.target,
            asset.document,
            registry);
        if (result.succeeded && result.module is not null)
        {
            asset.CommitDefinition(result.module.definition);
        }

        return result;
    }

    /// <summary>Compiles one neutral graph document into shared Shader IR.</summary>
    /// <param name="assetPath">Project-relative graph asset path used by diagnostics.</param>
    /// <param name="shaderName">Artist-facing generated shader name.</param>
    /// <param name="target">Graph output target.</param>
    /// <param name="document">Neutral graph document.</param>
    /// <param name="registry">Active built-in and script node registry snapshot.</param>
    /// <returns>Generated IR or node-mapped diagnostics.</returns>
    public static ShaderGraphCompileResult Compile(
        string assetPath,
        string shaderName,
        ShaderGraphTarget target,
        GraphDocument document,
        ShaderNodeRegistry registry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(assetPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(shaderName);
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(registry);
        List<ShaderDiagnostic> diagnostics = [];
        GraphValidationResult graphValidation = GraphValidator.Validate(
            document,
            registry,
            new ShaderGraphTypeConversion());
        foreach (GraphDiagnostic diagnostic in graphValidation.diagnostics)
        {
            bool missingNode = diagnostic.code == "GRAPH_MISSING_NODE";
            ShaderDiagnosticSeverity severity = diagnostic.severity == GraphDiagnosticSeverity.Error || missingNode
                ? ShaderDiagnosticSeverity.Error
                : diagnostic.severity == GraphDiagnosticSeverity.Warning
                    ? ShaderDiagnosticSeverity.Warning
                    : ShaderDiagnosticSeverity.Info;
            diagnostics.Add(new ShaderDiagnostic(
                $"SHADER_{diagnostic.code}",
                severity,
                diagnostic.message,
                diagnostic.nodeId is GraphNodeId nodeId
                    ? Location(assetPath, "Graph", StageFor(target), nodeId)
                    : null));
        }

        if (diagnostics.Any(static value => value.severity == ShaderDiagnosticSeverity.Error))
        {
            return new ShaderGraphCompileResult(null, diagnostics);
        }

        ShaderGraphOutputKind requiredOutput = target switch
        {
            ShaderGraphTarget.Surface => ShaderGraphOutputKind.Surface,
            ShaderGraphTarget.VertexFragment => ShaderGraphOutputKind.Fragment,
            ShaderGraphTarget.Compute => ShaderGraphOutputKind.Compute,
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        GraphNodeRecord[] outputs = document.nodes.Where(node =>
            registry.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
            && definition?.outputKind == requiredOutput).ToArray();
        if (outputs.Length != 1)
        {
            diagnostics.Add(new ShaderDiagnostic(
                "SHADER_GRAPH_OUTPUT_COUNT",
                ShaderDiagnosticSeverity.Error,
                $"A {target} graph requires exactly one {requiredOutput} output node; found {outputs.Length}."));
            return new ShaderGraphCompileResult(null, diagnostics);
        }

        GraphNodeRecord[] vertexOutputs = target == ShaderGraphTarget.Compute
            ? []
            : document.nodes.Where(node =>
                registry.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition)
                && definition?.outputKind == ShaderGraphOutputKind.Vertex).ToArray();
        if (vertexOutputs.Length > 1)
        {
            diagnostics.Add(new ShaderDiagnostic(
                "SHADER_GRAPH_VERTEX_OUTPUT_COUNT",
                ShaderDiagnosticSeverity.Error,
                $"A raster graph accepts at most one Vertex output node; found {vertexOutputs.Length}."));
            return new ShaderGraphCompileResult(null, diagnostics);
        }

        try
        {
            Emission emission = EmitGraph(
                assetPath,
                StageFor(target),
                document,
                registry,
                outputs[0],
                diagnostics);
            Emission? vertexEmission = vertexOutputs.Length == 0
                ? null
                : EmitGraph(
                    assetPath,
                    ShaderStage.Vertex,
                    document,
                    registry,
                    vertexOutputs[0],
                    diagnostics);
            if (diagnostics.Any(static value => value.severity == ShaderDiagnosticSeverity.Error))
            {
                return new ShaderGraphCompileResult(null, diagnostics);
            }

            ShaderIRModule module = GenerateModule(
                assetPath,
                shaderName,
                target,
                emission,
                vertexEmission);
            ShaderIRValidationResult validation = ShaderIRValidator.Validate(module);
            diagnostics.AddRange(validation.diagnostics);
            return diagnostics.Any(static value => value.severity == ShaderDiagnosticSeverity.Error)
                ? new ShaderGraphCompileResult(null, diagnostics)
                : new ShaderGraphCompileResult(module, diagnostics);
        }
        catch (Exception exception)
        {
            diagnostics.Add(new ShaderDiagnostic(
                "SHADER_GRAPH_EMIT_FAILED",
                ShaderDiagnosticSeverity.Error,
                $"Shader graph emission failed: {exception.Message}",
                Location(assetPath, "Graph", StageFor(target), outputs[0].id)));
            return new ShaderGraphCompileResult(null, diagnostics);
        }
    }

    private static Emission EmitGraph(
        string assetPath,
        ShaderStage stage,
        GraphDocument document,
        ShaderNodeRegistry registry,
        GraphNodeRecord output,
        List<ShaderDiagnostic> diagnostics)
    {
        IReadOnlyList<GraphNodeRecord> order = TopologicalOrder(document);
        HashSet<GraphNodeId> activeNodes = CollectAncestors(document, output.id);
        Dictionary<GraphEndpoint, ShaderValue> values = [];
        Dictionary<string, ShaderPropertyDefinition> properties = new(StringComparer.Ordinal);
        Dictionary<string, ShaderValue> semantics = new(StringComparer.Ordinal);
        List<string> statements = [];
        Dictionary<GraphNodeId, IReadOnlyDictionary<GraphPortId, GraphPortDefinition>> ports = [];
        foreach (GraphNodeRecord node in document.nodes)
        {
            registry.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition);
            ports[node.id] = definition!.GetPorts(node).ToDictionary(static value => value.id);
        }

        foreach (GraphNodeRecord node in order)
        {
            if (!activeNodes.Contains(node.id))
            {
                continue;
            }

            registry.TryResolveShader(node.definitionId, out ShaderNodeDefinition? definition);
            if (definition is null)
            {
                continue;
            }

            if ((definition.supportedStages & stage) == 0)
            {
                diagnostics.Add(new ShaderDiagnostic(
                    "SHADER_GRAPH_STAGE_ILLEGAL",
                    ShaderDiagnosticSeverity.Error,
                    $"Node '{definition.displayName}' cannot execute in the {stage} stage.",
                    Location(assetPath, "Graph", stage, node.id)));
                continue;
            }

            Dictionary<GraphPortId, ShaderValue> inputs = [];
            foreach (GraphEdgeRecord edge in document.edges.Where(candidate => candidate.input.nodeId == node.id))
            {
                if (!values.TryGetValue(edge.output, out ShaderValue source))
                {
                    throw new InvalidOperationException($"Upstream value '{edge.output}' was not emitted.");
                }

                GraphPortDefinition inputPort = ports[node.id][edge.input.portId];
                ShaderValueType destinationType = ShaderGraphValueTypes.Parse(inputPort.valueTypeId);
                inputs[edge.input.portId] = Convert(source, destinationType);
            }

            var context = new ShaderNodeEmitContext(
                node,
                stage,
                inputs,
                (port, value) => values[new GraphEndpoint(node.id, port)] = value,
                statements.Add,
                property => DeclareProperty(properties, property),
                (semantic, value) =>
                {
                    if (node.id == output.id)
                    {
                        semantics[semantic] = value;
                    }
                });
            try
            {
                definition.Emit(context);
            }
            catch (Exception exception)
            {
                diagnostics.Add(new ShaderDiagnostic(
                    "SHADER_GRAPH_NODE_EMIT_FAILED",
                    ShaderDiagnosticSeverity.Error,
                    $"Node '{definition.displayName}' failed: {exception.Message}",
                    Location(assetPath, "Graph", stage, node.id)));
            }
        }

        return new Emission(properties.Values.ToArray(), semantics, statements, output.id);
    }

    private static ShaderIRModule GenerateModule(
        string assetPath,
        string shaderName,
        ShaderGraphTarget target,
        Emission emission,
        Emission? vertexEmission)
    {
        List<ShaderPropertyDefinition> properties = MergeProperties(
            emission.properties,
            vertexEmission?.properties ?? Array.Empty<ShaderPropertyDefinition>());
        if (target == ShaderGraphTarget.Surface)
        {
            AddBuiltinProperty(properties, "inno_camera_position", "Camera Position");
            AddBuiltinProperty(properties, "inno_main_light_direction", "Main Light Direction");
            AddBuiltinProperty(properties, "inno_main_light_color", "Main Light Color");
            AddBuiltinProperty(properties, "inno_light_count", "Light Count");
            AddBuiltinProperty(properties, "inno_object_id", "Object ID");
            AddBuiltinProperty(properties, "inno_view_parameters", "View Parameters");
            AddBuiltinProperty(properties, "inno_cluster_parameters", "Cluster Dimensions");
            AddBuiltinProperty(properties, "inno_cluster_depth_parameters", "Cluster Depth Range");
            AddBuiltinProperty(
                properties,
                "inno_cluster_grid",
                "Cluster Grid",
                ShaderPropertyType.Buffer,
                "null");
            AddBuiltinProperty(
                properties,
                "inno_cluster_light_indices",
                "Cluster Light Indices",
                ShaderPropertyType.Buffer,
                "null");
            AddBuiltinProperty(
                properties,
                "inno_shadow_atlas",
                "Directional Shadow Atlas",
                ShaderPropertyType.Texture2DArray,
                "null");
            AddBuiltinProperty(properties, "inno_shadow_cascade_splits", "Shadow Cascade Splits");
            AddBuiltinProperty(properties, "inno_shadow_parameters", "Shadow Parameters");
            for (int index = 0; index < 4; index++)
            {
                AddBuiltinProperty(
                    properties,
                    $"inno_shadow_matrix_{index}",
                    $"Shadow Matrix {index}",
                    ShaderPropertyType.Matrix4x4,
                    "[1,0,0,0,0,1,0,0,0,0,1,0,0,0,0,1]");
            }

            for (int index = 0; index < C_MAX_LOCAL_LIGHTS; index++)
            {
                AddBuiltinProperty(
                    properties,
                    $"inno_local_light_position_range_{index}",
                    $"Local Light {index} Position and Range");
                AddBuiltinProperty(
                    properties,
                    $"inno_local_light_direction_outer_{index}",
                    $"Local Light {index} Direction and Outer Cone");
                AddBuiltinProperty(
                    properties,
                    $"inno_local_light_color_inner_{index}",
                    $"Local Light {index} Color and Inner Cone");
            }
        }

        List<ShaderPassDefinition> definitions = target switch
        {
            ShaderGraphTarget.Surface => SurfaceDefinitions(),
            ShaderGraphTarget.VertexFragment =>
            [
                new ShaderPassDefinition(
                    "GraphForward",
                    BuiltinShaderPassTags.ForwardLit,
                    null,
                    null,
                    null,
                    null)
            ],
            ShaderGraphTarget.Compute =>
            [
                new ShaderPassDefinition(
                    "GraphCompute",
                    BuiltinShaderPassTags.Compute,
                    null,
                    null,
                    null,
                    null,
                    GraphicsFeature.Compute | GraphicsFeature.StorageBuffer)
            ],
            _ => throw new ArgumentOutOfRangeException(nameof(target))
        };
        var definition = new ShaderDefinition(
            shaderName,
            properties,
            Array.Empty<ShaderKeywordDefinition>(),
            definitions);
        List<ShaderIRPass> passes = [];
        foreach (ShaderPassDefinition pass in definitions)
        {
            if (target == ShaderGraphTarget.Compute)
            {
                string source = ComputeSource(properties, emission);
                passes.Add(new ShaderIRPass(
                    pass,
                    [Stage(assetPath, pass.name, ShaderStage.Compute, source, emission.outputNodeId)]));
                continue;
            }

            IReadOnlyList<ShaderPropertyDefinition> passProperties = target == ShaderGraphTarget.Surface
                ? PropertiesForSurfacePass(properties, pass.tag)
                : properties;
            string vertexSource = VertexSource(passProperties, vertexEmission);
            string fragmentSource = target == ShaderGraphTarget.Surface
                ? SurfaceFragmentSource(pass.tag, passProperties, emission)
                : FragmentSource(passProperties, emission);
            passes.Add(new ShaderIRPass(
                pass,
                [
                    Stage(
                        assetPath,
                        pass.name,
                        ShaderStage.Vertex,
                        vertexSource,
                        vertexEmission?.outputNodeId ?? emission.outputNodeId),
                    Stage(assetPath, pass.name, ShaderStage.Fragment, fragmentSource, emission.outputNodeId)
                ],
                C_VARYING_SOURCE,
                passProperties.Select(static property => property.id).ToArray()));
        }

        return new ShaderIRModule(definition, passes);
    }

    private static string VertexSource(
        IReadOnlyList<ShaderPropertyDefinition> properties,
        Emission? emission)
    {
        string position = emission?.semantics["position"].expression ?? "a_position";
        return $$"""
        $input a_position, a_normal, a_tangent, a_texcoord0
        $output v_worldNormal, v_texcoord0, v_worldPosition
        #include <bgfx_shader.sh>
        {{Declarations(properties, ShaderStage.Vertex, writableBuffers: false)}}
        void main()
        {
            {{Statements(emission?.statements ?? Array.Empty<string>())}}
            vec4 worldPosition = mul(u_model[0], vec4({{position}}, 1.0));
            v_worldPosition = worldPosition.xyz;
            v_worldNormal = normalize(mul(u_model[0], vec4(a_normal, 0.0)).xyz);
            v_texcoord0 = a_texcoord0;
            gl_Position = mul(u_viewProj, worldPosition);
        }
        """;
    }

    private static string FragmentSource(
        IReadOnlyList<ShaderPropertyDefinition> properties,
        Emission emission)
    {
        ShaderValue color = emission.semantics["color"];
        return $$"""
        $input v_worldNormal, v_texcoord0, v_worldPosition
        #include <bgfx_shader.sh>
        {{Declarations(properties, ShaderStage.Fragment, writableBuffers: false)}}
        void main()
        {
            {{Statements(emission.statements)}}
            gl_FragColor = {{color.expression}};
        }
        """;
    }

    private static string SurfaceFragmentSource(
        string passTag,
        IReadOnlyList<ShaderPropertyDefinition> properties,
        Emission emission)
    {
        string baseColor = emission.semantics["baseColor"].expression;
        string metallic = emission.semantics["metallic"].expression;
        string roughness = emission.semantics["roughness"].expression;
        string emissionValue = emission.semantics["emission"].expression;
        string occlusion = emission.semantics["occlusion"].expression;
        string alpha = emission.semantics["alpha"].expression;
        bool clustered = string.Equals(
            passTag,
            BuiltinShaderPassTags.ForwardLitClustered,
            StringComparison.Ordinal);
        string localInterfaceKeep = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, C_MAX_LOCAL_LIGHTS).Select(static index =>
                $"    inno_keep_interface += inno_local_light_position_range_{index}.x"
                + $" + inno_local_light_direction_outer_{index}.x"
                + $" + inno_local_light_color_inner_{index}.x;"));
        string keepInterface = $$"""
            float inno_keep_interface = {{baseColor}}.x + {{metallic}} + {{roughness}}
                + {{emissionValue}}.x + {{occlusion}} + {{alpha}}
                + inno_camera_position.x + inno_main_light_direction.x
                + inno_main_light_color.x + inno_light_count.x + inno_object_id.x
                + inno_view_parameters.x + inno_shadow_cascade_splits.x
                + inno_shadow_parameters.x + inno_shadow_matrix_0[0][0]
                + inno_shadow_matrix_1[0][0] + inno_shadow_matrix_2[0][0]
                + inno_shadow_matrix_3[0][0];
            {{localInterfaceKeep}}
            if (inno_keep_interface < -0.5)
            {
                inno_keep_interface += texture2DArray(
                    inno_shadow_atlas,
                    vec3(0.5, 0.5, 0.0)).x;
            }
            if (inno_keep_interface < -1.0) discard;
            """;
        string localLighting = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, C_MAX_LOCAL_LIGHTS).Select(index => $$"""
                if (inno_light_count.x > {{index}}.5)
                {
                    lighting += InnoEvaluateLocalLight(
                        v_worldPosition,
                        N,
                        V,
                        {{baseColor}}.rgb,
                        metallic,
                        perceptualRoughness,
                        inno_local_light_position_range_{{index}},
                        inno_local_light_direction_outer_{{index}},
                        inno_local_light_color_inner_{{index}});
                }
                """));
        string clusteredLighting = ClusteredLocalLighting(
            "lighting",
            "v_worldPosition",
            "N",
            "V",
            $"{baseColor}.rgb",
            "metallic",
            "perceptualRoughness");
        string output = passTag switch
        {
            BuiltinShaderPassTags.ForwardLitClustered => $$"""
                vec3 N = normalize(v_worldNormal);
                vec3 V = normalize(inno_camera_position.xyz - v_worldPosition);
                float metallic = clamp({{metallic}}, 0.0, 1.0);
                float perceptualRoughness = clamp({{roughness}}, 0.04, 1.0);
                vec3 lighting = vec3(0.0);
                if (inno_light_count.y > 0.5)
                {
                    lighting += InnoEvaluatePbr(
                        {{baseColor}}.rgb,
                        metallic,
                        perceptualRoughness,
                        N,
                        V,
                        normalize(-inno_main_light_direction.xyz),
                        inno_main_light_color.rgb
                            * InnoEvaluateDirectionalShadow(v_worldPosition));
                }
                {{clusteredLighting}}
                gl_FragColor = vec4((lighting * {{occlusion}}) + {{emissionValue}}.rgb, {{alpha}});
                {{keepInterface}}
                """,
            BuiltinShaderPassTags.ForwardLit => $$"""
                vec3 N = normalize(v_worldNormal);
                vec3 V = normalize(inno_camera_position.xyz - v_worldPosition);
                float metallic = clamp({{metallic}}, 0.0, 1.0);
                float perceptualRoughness = clamp({{roughness}}, 0.04, 1.0);
                vec3 lighting = vec3(0.0);
                if (inno_light_count.y > 0.5)
                {
                    lighting += InnoEvaluatePbr(
                        {{baseColor}}.rgb,
                        metallic,
                        perceptualRoughness,
                        N,
                        V,
                        normalize(-inno_main_light_direction.xyz),
                        inno_main_light_color.rgb
                            * InnoEvaluateDirectionalShadow(v_worldPosition));
                }
                {{localLighting}}
                gl_FragColor = vec4((lighting * {{occlusion}}) + {{emissionValue}}.rgb, {{alpha}});
                {{keepInterface}}
                """,
            BuiltinShaderPassTags.GBuffer => $$"""
                gl_FragData[0] = vec4({{baseColor}}.rgb, clamp({{metallic}}, 0.0, 1.0));
                gl_FragData[1] = vec4(normalize(v_worldNormal) * 0.5 + 0.5, clamp({{roughness}}, 0.0, 1.0));
                gl_FragData[2] = vec4({{emissionValue}}.rgb, clamp({{occlusion}}, 0.0, 1.0));
                {{keepInterface}}
                """,
            BuiltinShaderPassTags.Picking => $$"""
                gl_FragColor = inno_object_id;
                {{keepInterface}}
                """
            ,
            _ => keepInterface
        };
        return $$"""
        $input v_worldNormal, v_texcoord0, v_worldPosition
        #include <{{(clustered ? "bgfx_compute.sh" : "bgfx_shader.sh")}}>
        {{Declarations(properties, ShaderStage.Fragment, writableBuffers: false)}}
        {{PbrFunctions()}}
        void main()
        {
            {{Statements(emission.statements)}}
            {{output}}
        }
        """;
    }

    private static string PbrFunctions()
        => """
        float InnoDistributionGgx(float NoH, float roughness)
        {
            float alpha = roughness * roughness;
            float alphaSquared = alpha * alpha;
            float denominator = NoH * NoH * (alphaSquared - 1.0) + 1.0;
            return alphaSquared / max(3.14159265 * denominator * denominator, 0.00001);
        }

        float InnoGeometrySchlickGgx(float NoV, float roughness)
        {
            float radius = roughness + 1.0;
            float k = radius * radius * 0.125;
            return NoV / max(NoV * (1.0 - k) + k, 0.00001);
        }

        vec3 InnoFresnelSchlick(float cosine, vec3 f0)
        {
            return f0 + (1.0 - f0) * pow(1.0 - saturate(cosine), 5.0);
        }

        vec3 InnoEvaluatePbr(
            vec3 baseColor,
            float metallic,
            float roughness,
            vec3 normal,
            vec3 viewDirection,
            vec3 lightDirection,
            vec3 radiance)
        {
            vec3 halfDirection = normalize(viewDirection + lightDirection);
            float NoL = max(dot(normal, lightDirection), 0.0);
            float NoV = max(dot(normal, viewDirection), 0.0001);
            float NoH = max(dot(normal, halfDirection), 0.0);
            float VoH = max(dot(viewDirection, halfDirection), 0.0);
            vec3 f0 = mix(vec3(0.04), baseColor, metallic);
            vec3 fresnel = InnoFresnelSchlick(VoH, f0);
            float distribution = InnoDistributionGgx(NoH, roughness);
            float geometry = InnoGeometrySchlickGgx(NoV, roughness)
                * InnoGeometrySchlickGgx(NoL, roughness);
            vec3 specular = distribution * geometry * fresnel / max(4.0 * NoV * NoL, 0.0001);
            vec3 diffuseWeight = (vec3(1.0) - fresnel) * (1.0 - metallic);
            return (diffuseWeight * baseColor / 3.14159265 + specular) * radiance * NoL;
        }

        vec3 InnoEvaluateLocalLight(
            vec3 worldPosition,
            vec3 normal,
            vec3 viewDirection,
            vec3 baseColor,
            float metallic,
            float roughness,
            vec4 positionRange,
            vec4 directionOuter,
            vec4 colorInner)
        {
            vec3 toLight = positionRange.xyz - worldPosition;
            float distanceSquared = max(dot(toLight, toLight), 0.0001);
            vec3 lightDirection = toLight * inversesqrt(distanceSquared);
            float rangeSquared = max(positionRange.w * positionRange.w, 0.0001);
            float rangeFactor = saturate(1.0 - distanceSquared / rangeSquared);
            float attenuation = rangeFactor * rangeFactor / max(distanceSquared, 1.0);
            float spot = colorInner.w < 0.0
                ? 1.0
                : smoothstep(
                    directionOuter.w,
                    colorInner.w,
                    dot(normalize(directionOuter.xyz), -lightDirection));
            return InnoEvaluatePbr(
                baseColor,
                metallic,
                roughness,
                normal,
                viewDirection,
                lightDirection,
                colorInner.rgb * attenuation * spot);
        }

        float InnoEvaluateDirectionalShadow(vec3 worldPosition)
        {
            if (inno_shadow_parameters.x < 0.5 || inno_shadow_parameters.y <= 0.0)
            {
                return 1.0;
            }

            float viewDistance = abs(mul(u_view, vec4(worldPosition, 1.0)).z);
            float cascade = 0.0;
            mat4 worldToShadow = inno_shadow_matrix_0;
            if (viewDistance > inno_shadow_cascade_splits.x && inno_shadow_parameters.x > 1.5)
            {
                cascade = 1.0;
                worldToShadow = inno_shadow_matrix_1;
            }
            if (viewDistance > inno_shadow_cascade_splits.y && inno_shadow_parameters.x > 2.5)
            {
                cascade = 2.0;
                worldToShadow = inno_shadow_matrix_2;
            }
            if (viewDistance > inno_shadow_cascade_splits.z && inno_shadow_parameters.x > 3.5)
            {
                cascade = 3.0;
                worldToShadow = inno_shadow_matrix_3;
            }

            vec4 shadowPosition = mul(worldToShadow, vec4(worldPosition, 1.0));
            vec3 shadowNdc = shadowPosition.xyz / max(abs(shadowPosition.w), 0.00001);
            vec2 shadowUv = shadowNdc.xy * 0.5 + 0.5;
            if (inno_view_parameters.w < 0.5)
            {
                shadowUv.y = 1.0 - shadowUv.y;
            }

            float receiverDepth = inno_view_parameters.z > 0.5
                ? shadowNdc.z * 0.5 + 0.5
                : shadowNdc.z;
            if (shadowUv.x <= 0.0 || shadowUv.x >= 1.0
                || shadowUv.y <= 0.0 || shadowUv.y >= 1.0
                || receiverDepth <= 0.0 || receiverDepth >= 1.0)
            {
                return 1.0;
            }

            float visibility = 0.0;
            for (int y = -1; y <= 1; y += 2)
            {
                for (int x = -1; x <= 1; x += 2)
                {
                    vec2 offset = vec2(float(x), float(y)) * inno_shadow_parameters.w * 0.5;
                    float storedDepth = texture2DArray(
                        inno_shadow_atlas,
                        vec3(shadowUv + offset, cascade)).x;
                    visibility += receiverDepth - inno_shadow_parameters.z <= storedDepth ? 0.25 : 0.0;
                }
            }

            return mix(1.0, visibility, inno_shadow_parameters.y);
        }
        """;

    private static string ComputeSource(
        IReadOnlyList<ShaderPropertyDefinition> properties,
        Emission emission)
    {
        ShaderValue value = emission.semantics["value"];
        return $$"""
        #include <bgfx_compute.sh>
        {{Declarations(properties, ShaderStage.Compute, writableBuffers: true)}}
        NUM_THREADS(8, 8, 1)
        void main()
        {
            {{Statements(emission.statements)}}
            inno_compute_output[gl_GlobalInvocationID.x] = {{value.expression}};
        }
        """;
    }

    private static string Declarations(
        IReadOnlyList<ShaderPropertyDefinition> properties,
        ShaderStage stage,
        bool writableBuffers)
    {
        var builder = new StringBuilder();
        int resourceSlot = 0;
        foreach (ShaderPropertyDefinition property in properties.Where(value => (value.stages & stage) != 0))
        {
            switch (property.type)
            {
                case ShaderPropertyType.Texture2D:
                    builder.AppendLine($"SAMPLER2D({property.id.value}, {resourceSlot++});");
                    break;
                case ShaderPropertyType.Texture2DArray:
                    builder.AppendLine($"SAMPLER2DARRAY({property.id.value}, {resourceSlot++});");
                    break;
                case ShaderPropertyType.TextureCube:
                    builder.AppendLine($"SAMPLERCUBE({property.id.value}, {resourceSlot++});");
                    break;
                case ShaderPropertyType.Buffer:
                    string elementType = IsClusterBuffer(property.id.value) ? "uint" : "vec4";
                    builder.AppendLine(
                        $"{(writableBuffers ? "BUFFER_RW" : "BUFFER_RO")}({property.id.value}, {elementType}, {resourceSlot++});");
                    break;
                case ShaderPropertyType.Matrix4x4:
                    builder.AppendLine($"uniform mat4 {property.id.value};");
                    break;
                case ShaderPropertyType.Sampler:
                    builder.AppendLine($"SAMPLER2D({property.id.value}, {resourceSlot++});");
                    break;
                default:
                    builder.AppendLine($"uniform vec4 {property.id.value};");
                    break;
            }
        }

        return builder.ToString();
    }

    private static string Statements(IReadOnlyList<string> statements)
        => string.Join(Environment.NewLine, statements.Select(static statement => $"    {statement}"));

    private static ShaderIRStageModule Stage(
        string assetPath,
        string passName,
        ShaderStage stage,
        string source,
        GraphNodeId outputNodeId)
        => new(
            stage,
            "main",
            source,
            ShaderIRSourceKind.Generated,
            Location(assetPath, passName, stage, outputNodeId),
            new Dictionary<int, string> { [1] = outputNodeId.value });

    private static List<ShaderPassDefinition> SurfaceDefinitions()
        =>
        [
            new(
                "ForwardLitClustered",
                BuiltinShaderPassTags.ForwardLitClustered,
                null,
                null,
                null,
                null,
                GraphicsFeature.Compute | GraphicsFeature.StorageBuffer),
            new("ForwardLit", BuiltinShaderPassTags.ForwardLit, null, null, null, null),
            new("GBuffer", BuiltinShaderPassTags.GBuffer, null, null, null, null),
            new("DepthOnly", BuiltinShaderPassTags.DepthOnly, null, null, null, null),
            new("ShadowCaster", BuiltinShaderPassTags.ShadowCaster, null, null, null, null),
            new("Picking", BuiltinShaderPassTags.Picking, null, null, null, null)
        ];

    private static IReadOnlyList<ShaderPropertyDefinition> PropertiesForSurfacePass(
        IReadOnlyList<ShaderPropertyDefinition> properties,
        string passTag)
        => string.Equals(passTag, BuiltinShaderPassTags.ForwardLitClustered, StringComparison.Ordinal)
            ? properties
            : properties.Where(static property => !IsClusterBinding(property.id.value)).ToArray();

    private static bool IsClusterBinding(string id)
        => string.Equals(id, "inno_cluster_parameters", StringComparison.Ordinal)
            || string.Equals(id, "inno_cluster_depth_parameters", StringComparison.Ordinal)
            || IsClusterBuffer(id);

    private static bool IsClusterBuffer(string id)
        => string.Equals(id, "inno_cluster_grid", StringComparison.Ordinal)
            || string.Equals(id, "inno_cluster_light_indices", StringComparison.Ordinal);

    private static string ClusteredLocalLighting(
        string accumulator,
        string worldPosition,
        string normal,
        string viewDirection,
        string baseColor,
        string metallic,
        string roughness)
    {
        string branches = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, C_MAX_LOCAL_LIGHTS).Select(index => $$"""
                    if (inno_light_index == {{index}}u)
                    {
                        {{accumulator}} += InnoEvaluateLocalLight(
                            {{worldPosition}},
                            {{normal}},
                            {{viewDirection}},
                            {{baseColor}},
                            {{metallic}},
                            {{roughness}},
                            inno_local_light_position_range_{{index}},
                            inno_local_light_direction_outer_{{index}},
                            inno_local_light_color_inner_{{index}});
                    }
                """));
        return $$"""
            float inno_view_depth = max(abs(mul(u_view, vec4({{worldPosition}}, 1.0)).z),
                inno_cluster_depth_parameters.x);
            float inno_depth_scale = max(inno_cluster_depth_parameters.z, 0.0001);
            float inno_slice_value = log(inno_view_depth / inno_cluster_depth_parameters.x)
                / inno_depth_scale * inno_cluster_parameters.z;
            uint inno_slice = uint(clamp(
                floor(inno_slice_value),
                0.0,
                inno_cluster_parameters.z - 1.0));
            float inno_pixel_y = inno_view_parameters.w > 0.5
                ? inno_view_parameters.y - gl_FragCoord.y
                : gl_FragCoord.y;
            uint inno_tile_x = uint(clamp(
                floor(gl_FragCoord.x / inno_cluster_parameters.w),
                0.0,
                inno_cluster_parameters.x - 1.0));
            uint inno_tile_y = uint(clamp(
                floor(inno_pixel_y / inno_cluster_parameters.w),
                0.0,
                inno_cluster_parameters.y - 1.0));
            uint inno_cluster_index = inno_tile_x
                + inno_tile_y * uint(inno_cluster_parameters.x)
                + inno_slice * uint(inno_cluster_parameters.x * inno_cluster_parameters.y);
            uint inno_grid_offset = inno_cluster_grid[inno_cluster_index * 2u];
            uint inno_grid_count = min(
                inno_cluster_grid[inno_cluster_index * 2u + 1u],
                uint(inno_cluster_depth_parameters.w));
            for (uint inno_list_index = 0u; inno_list_index < inno_grid_count; ++inno_list_index)
            {
                uint inno_light_index = inno_cluster_light_indices[inno_grid_offset + inno_list_index];
            {{branches}}
            }
            """;
    }

    private static void AddBuiltinProperty(
        ICollection<ShaderPropertyDefinition> properties,
        string id,
        string displayName,
        ShaderPropertyType type = ShaderPropertyType.Vector4,
        string defaultValueJson = "[0,0,0,0]")
    {
        if (properties.Any(property => property.id.value == id))
        {
            throw new InvalidOperationException($"Shader graph property '{id}' is reserved by the pipeline.");
        }

        properties.Add(new ShaderPropertyDefinition(
            new ShaderPropertyId(id),
            displayName,
            type,
            ShaderStage.Fragment,
            defaultValueJson));
    }

    private static List<ShaderPropertyDefinition> MergeProperties(
        IReadOnlyList<ShaderPropertyDefinition> primary,
        IReadOnlyList<ShaderPropertyDefinition> secondary)
    {
        Dictionary<string, ShaderPropertyDefinition> merged = new(StringComparer.Ordinal);
        foreach (ShaderPropertyDefinition property in primary.Concat(secondary))
        {
            if (!merged.TryGetValue(property.id.value, out ShaderPropertyDefinition? current))
            {
                merged.Add(property.id.value, property);
                continue;
            }

            if (current.type != property.type
                || !string.Equals(current.defaultValueJson, property.defaultValueJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Shader property '{property.id.value}' has conflicting stage declarations.");
            }

            merged[property.id.value] = new ShaderPropertyDefinition(
                current.id,
                current.displayName,
                current.type,
                current.stages | property.stages,
                current.defaultValueJson);
        }

        return merged.Values.ToList();
    }

    private static ShaderStage StageFor(ShaderGraphTarget target)
        => target == ShaderGraphTarget.Compute ? ShaderStage.Compute : ShaderStage.Fragment;

    private static ShaderSourceLocation Location(
        string assetPath,
        string passName,
        ShaderStage stage,
        GraphNodeId nodeId)
        => new(assetPath, passName, stage, nodeId: nodeId.value);

    private static void DeclareProperty(
        IDictionary<string, ShaderPropertyDefinition> properties,
        ShaderPropertyDefinition property)
    {
        if (properties.TryGetValue(property.id.value, out ShaderPropertyDefinition? current))
        {
            if (current.type != property.type
                || !string.Equals(current.defaultValueJson, property.defaultValueJson, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Shader property '{property.id.value}' has conflicting declarations.");
            }

            return;
        }

        properties.Add(property.id.value, property);
    }

    private static ShaderValue Convert(ShaderValue source, ShaderValueType destination)
    {
        if (source.type == destination
            || source.type == ShaderValueType.Float4 && destination == ShaderValueType.Color
            || source.type == ShaderValueType.Color && destination == ShaderValueType.Float4)
        {
            return new ShaderValue(destination, source.expression, source.sourceNodeId);
        }

        string expression = (source.type, destination) switch
        {
            (ShaderValueType.Float, ShaderValueType.Float2) => $"vec2({source.expression})",
            (ShaderValueType.Float, ShaderValueType.Float3) => $"vec3({source.expression})",
            (ShaderValueType.Float, ShaderValueType.Float4 or ShaderValueType.Color) =>
                $"vec4({source.expression})",
            (ShaderValueType.Float3, ShaderValueType.Float4 or ShaderValueType.Color) =>
                $"vec4({source.expression}, 1.0)",
            _ => throw new InvalidOperationException(
                $"No shader conversion exists from {source.type} to {destination}.")
        };
        return new ShaderValue(destination, expression, source.sourceNodeId);
    }

    private static IReadOnlyList<GraphNodeRecord> TopologicalOrder(GraphDocument document)
    {
        Dictionary<GraphNodeId, int> indegree = document.nodes.ToDictionary(static node => node.id, static _ => 0);
        Dictionary<GraphNodeId, List<GraphNodeId>> adjacency = document.nodes.ToDictionary(
            static node => node.id,
            static _ => new List<GraphNodeId>());
        foreach (GraphEdgeRecord edge in document.edges)
        {
            adjacency[edge.output.nodeId].Add(edge.input.nodeId);
            indegree[edge.input.nodeId]++;
        }

        Queue<GraphNodeRecord> ready = new(document.nodes.Where(node => indegree[node.id] == 0));
        List<GraphNodeRecord> result = [];
        while (ready.TryDequeue(out GraphNodeRecord? node))
        {
            result.Add(node);
            foreach (GraphNodeId target in adjacency[node.id])
            {
                indegree[target]--;
                if (indegree[target] == 0)
                {
                    ready.Enqueue(document.FindNode(target)!);
                }
            }
        }

        return result.Count == document.nodes.Count
            ? result
            : throw new InvalidOperationException("Shader graph topological ordering failed.");
    }

    private static HashSet<GraphNodeId> CollectAncestors(GraphDocument document, GraphNodeId outputNodeId)
    {
        Dictionary<GraphNodeId, List<GraphNodeId>> incoming = document.nodes.ToDictionary(
            static node => node.id,
            static _ => new List<GraphNodeId>());
        foreach (GraphEdgeRecord edge in document.edges)
        {
            incoming[edge.input.nodeId].Add(edge.output.nodeId);
        }

        HashSet<GraphNodeId> active = [];
        Stack<GraphNodeId> pending = new();
        pending.Push(outputNodeId);
        while (pending.TryPop(out GraphNodeId current))
        {
            if (!active.Add(current))
            {
                continue;
            }

            foreach (GraphNodeId source in incoming[current])
            {
                pending.Push(source);
            }
        }

        return active;
    }

    private sealed record Emission(
        IReadOnlyList<ShaderPropertyDefinition> properties,
        IReadOnlyDictionary<string, ShaderValue> semantics,
        IReadOnlyList<string> statements,
        GraphNodeId outputNodeId);
}
