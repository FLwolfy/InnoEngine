using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Core.Diagnostics;
using Inno.Core.Execution;
using Inno.Editor.Core;
using Inno.Editor.Rendering;
using Inno.Extensibility.Types;
using Inno.Rendering;
using UI = Inno.Native.ImGui.ImGui;
using Widget = Inno.Editor.ImGui.ImGuiWidget.ImGuiWidget;

namespace Inno.Editor.Shaders;

/// <summary>Hosts isolated Shader and Material images using domain-contributed preview pipelines.</summary>
[EditorModule("rendering.shader-previews", order: 176)]
public sealed class ShaderPreviews : EditorModule
{
    private readonly TypeCatalog m_types;
    private readonly Registry m_registry;
    private readonly IEditorPreviewService m_previews;
    private readonly EditorShaderCompilation m_compilation;
    private readonly Dictionary<Guid, State> m_states = [];
    private ulong m_frame;

    internal ShaderPreviews(TypeCatalog types, IEditorPreviewService previews, EditorShaderCompilation compilation)
    { m_types = types; m_registry = new(types); m_previews = previews; m_compilation = compilation; }

    /// <summary>Draws detached Material overrides using its Shader's compiled contract.</summary>
    /// <param name="ownerId">Persistent document identity; no Material is retained under this identity.</param>
    /// <param name="material">Invocation-local Material values.</param>
    /// <param name="logicalSize">Positive square image size in logical pixels.</param>
    public void DrawMaterial(Guid ownerId, MaterialAsset material, float logicalSize)
    {
        if (material.shader is null || material.shader.isMissing) { Widget.Hint("Preview requires an available Shader."); return; }
        try { Draw(ownerId, material, m_compilation.RequestArtifact(material.shader, RenderShaderVariant.FromMaterial(material)), logicalSize); }
        catch (Exception error) when (Recoverable(error)) { Widget.Hint("Preview: " + error.Message); }
    }

    /// <summary>Draws a compiled draft without modifying the source Shader or its canonical runtime publication.</summary>
    /// <param name="ownerId">Persistent document identity, independent of the canonical Shader publication.</param>
    /// <param name="material">Detached defaults or overrides with the current Shader reference.</param>
    /// <param name="compilation">Isolated draft compiler result, including explicit last-good state.</param>
    /// <param name="logicalSize">Positive square image size in logical pixels.</param>
    public void Draw(Guid ownerId, MaterialAsset material, EditorShaderDraftCompilationSnapshot compilation, float logicalSize)
    {
        if (ownerId == Guid.Empty) throw new ArgumentException("A preview owner identity is required.", nameof(ownerId));
        ArgumentNullException.ThrowIfNull(material); ArgumentNullException.ThrowIfNull(compilation);
        if (!(logicalSize > 0f) || !float.IsFinite(logicalSize)) throw new ArgumentOutOfRangeException(nameof(logicalSize));
        if (compilation.artifact is not { } artifact) { Widget.Hint("Preview: " + compilation.state); return; }
        if (!m_states.TryGetValue(ownerId, out State? state)) m_states.Add(ownerId, state = new("shader-preview/" + ownerId.ToString("N")));
        state.frame = m_frame;
        try
        {
            using IDisposable operation = m_types.AcquireOperation("Build isolated Shader preview");
            ShaderDefinition definition = m_compilation.ReadDefinition(artifact);
            string[] contracts = definition.techniques.Where(value => !material.techniqueId.isValid || value.id == material.techniqueId)
                .Select(value => value.contract.value).Distinct(StringComparer.Ordinal).Where(m_registry.providers.ContainsKey).ToArray();
            if (contracts.Length == 0) { Widget.Hint("No preview provider for this Shader contract."); return; }
            if (contracts.Length != 1) { Widget.Hint("Select a Technique to choose an unambiguous preview contract."); return; }
            int pixels = Math.Clamp((int)MathF.Ceiling(logicalSize * UI.GetWindowDpiScale()), 1, 2048);
            var context = new ShaderPreviewContext(new(state.viewportId), material, artifact, definition, state, pixels, pixels);
            EditorViewportLayer layer = m_registry.providers[contracts[0]].CreateLayer(context);
            if (m_previews.TryRender(new(state.viewportId, pixels, pixels, RenderTextureFormat.RGBA8, [layer]), out var handle))
                m_previews.Draw(handle, new(logicalSize, logicalSize));
            else UI.Dummy(new(logicalSize, logicalSize));
            Widget.Hint(compilation.usingLastGood ? "Draft preview · last-good candidate · Scene/Game unchanged" : "Isolated preview · Scene/Game unchanged");
            foreach (Diagnostic error in state.errors.Values) UI.TextWrapped(error.message);
        }
        catch (Exception error) when (Recoverable(error)) { Widget.Hint("Preview: " + error.Message); }
    }

    /// <summary>Releases this document's viewport and scoped GPU publication without touching other consumers.</summary>
    /// <param name="ownerId">Closing or disabled preview owner.</param>
    public void Release(Guid ownerId)
    {
        if (m_states.Remove(ownerId, out State? state)) m_previews.ReleaseRendered(state.viewportId);
    }

    /// <inheritdoc />
    protected override void OnUpdate(EditorContext context)
    {
        m_frame++;
        foreach (Guid id in m_states.Where(pair => m_frame - pair.Value.frame > 2).Select(pair => pair.Key).ToArray()) Release(id);
    }
    /// <inheritdoc />
    protected override void OnStop(EditorContext context)
    {
        foreach (Guid id in m_states.Keys.ToArray()) Release(id);
        m_registry.Dispose();
    }

    private static bool Recoverable(Exception error) => RetirementPendingException.Find(error) is null
        && error is InvalidOperationException or ArgumentException or FormatException;

    private sealed class State(string viewportId) : IDiagnosticReporter
    {
        internal readonly string viewportId = viewportId;
        internal readonly Dictionary<(string code, string? semanticId, Guid? objectId), Diagnostic> errors = [];
        internal ulong frame;
        public void Publish(Diagnostic diagnostic) => errors[(diagnostic.code, diagnostic.semanticId, diagnostic.objectId)] = diagnostic;
        public void Resolve(string code, string? semanticId = null, Guid? objectId = null) => errors.Remove((code, semanticId, objectId));
        public void Replace(IEnumerable<Diagnostic> diagnostics) { errors.Clear(); foreach (Diagnostic value in diagnostics) Publish(value); }
    }

    private sealed class Registry(TypeCatalog types) : TypeRegistry<IReadOnlyDictionary<string, ShaderPreviewProvider>>(types)
    {
        internal IReadOnlyDictionary<string, ShaderPreviewProvider> providers => current;
        protected override IReadOnlyDictionary<string, ShaderPreviewProvider> Build(TypeCacheSnapshot snapshot)
        {
            var result = new Dictionary<string, ShaderPreviewProvider>(StringComparer.Ordinal);
            try
            {
                foreach (Type type in snapshot.GetTypesWithAttribute<ShaderPreviewProviderAttribute>().Select(value => value.Resolve(snapshot)))
                {
                    string id = type.GetCustomAttribute<ShaderPreviewProviderAttribute>()!.contractId;
                    if (result.ContainsKey(id)) throw new InvalidOperationException("Duplicate Shader preview contract: " + id);
                    result.Add(id, CreateExtension<ShaderPreviewProvider>(type));
                }
                return result;
            }
            catch (Exception failure)
            {
                try { DisposeExtensions(result.Values); }
                catch (Exception retirement) { throw new AggregateException(failure, retirement); }
                throw;
            }
        }
        protected override void DisposeSnapshot(IReadOnlyDictionary<string, ShaderPreviewProvider> snapshot) => DisposeExtensions(snapshot.Values);
    }
}
