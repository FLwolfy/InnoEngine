using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Inno.Extensibility.Types;

namespace Inno.Rendering.ShaderGraph;

internal sealed class ShaderNodeExtensionRegistry
    : TypeRegistry<ShaderNodeExtensionRegistry.Snapshot>
{
    private readonly ShaderNodeRegistry m_owner;
    private Snapshot? m_transitionPrevious;

    internal ShaderNodeExtensionRegistry(ShaderNodeRegistry owner, TypeCatalog types)
        : base(types)
    {
        m_owner = owner ?? throw new ArgumentNullException(nameof(owner));
    }

    /// <summary>
    /// Builds a validated result from the current immutable input snapshot.
    /// </summary>
    /// <param name="types">
    /// The active type catalog generation used for extension resolution.
    /// </param>
    /// <returns>
    /// The validated snapshot that represents the completed operation.
    /// </returns>
    protected override Snapshot Build(TypeCacheSnapshot types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var definitions = new List<ShaderNodeDefinition>();
        try
        {
            foreach (TypeRef typeRef in types.GetTypesWithAttribute<ShaderNodeExtensionAttribute>())
            {
                Type type = typeRef.Resolve(types);
                ShaderNodeExtensionAttribute attribute = type.GetCustomAttribute<ShaderNodeExtensionAttribute>(
                    inherit: false)!;
                ShaderNodeDefinition definition = CreateExtension<ShaderNodeDefinition>(type);
                definitions.Add(definition);
                if (!string.Equals(attribute.id, definition.id, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Shader node extension '{type.FullName}' declares stable ID '{attribute.id}' " +
                        $"but its definition uses '{definition.id}'.");
                }
            }

            return Snapshot.Create(definitions);
        }
        catch
        {
            DisposeDefinitions(definitions);
            throw;
        }
    }

    /// <summary>
    /// Validates and prepares a candidate snapshot before it can become active.
    /// </summary>
    /// <param name="previous">
    /// The previous consumed by on activating; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="candidate">
    /// The candidate consumed by on activating; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    protected override void OnActivating(Snapshot? previous, Snapshot candidate)
    {
        if (m_transitionPrevious is not null)
        {
            throw new InvalidOperationException("A shader node registry transition is already active.");
        }

        m_transitionPrevious = m_owner.Activate(candidate);
        if (previous is not null && !ReferenceEquals(previous, m_transitionPrevious))
        {
            _ = m_owner.Activate(m_transitionPrevious);
            m_transitionPrevious = null;
            throw new InvalidOperationException("The shader node registry active generation is inconsistent.");
        }
    }

    /// <summary>
    /// Restores state retained for the previous snapshot after candidate activation fails.
    /// </summary>
    /// <param name="previous">
    /// The previous consumed by on activation rolled back; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="candidate">
    /// The candidate consumed by on activation rolled back; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    protected override void OnActivationRolledBack(Snapshot? previous, Snapshot candidate)
    {
        _ = previous;
        if (m_transitionPrevious is null)
        {
            return;
        }

        Snapshot rejected = m_owner.Activate(m_transitionPrevious);
        m_transitionPrevious = null;
        if (!ReferenceEquals(rejected, candidate))
        {
            throw new InvalidOperationException("Shader node registry rollback restored an unexpected generation.");
        }
    }

    /// <summary>
    /// Releases previous-generation state after the candidate becomes active.
    /// </summary>
    /// <param name="previous">
    /// The previous consumed by on activation completed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    /// <param name="currentSnapshot">
    /// The current snapshot consumed by on activation completed; ownership remains with the caller unless explicitly stated otherwise.
    /// </param>
    protected override void OnActivationCompleted(Snapshot? previous, Snapshot currentSnapshot)
    {
        _ = currentSnapshot;
        Snapshot? replaced = m_transitionPrevious;
        m_transitionPrevious = null;
        if (previous is null)
        {
            replaced?.Dispose();
        }
    }

    private static void DisposeDefinitions(IEnumerable<ShaderNodeDefinition> definitions)
    {
        List<Exception>? failures = null;
        foreach (IDisposable disposable in definitions.OfType<IDisposable>())
        {
            try
            {
                disposable.Dispose();
            }
            catch (Exception exception)
            {
                (failures ??= []).Add(exception);
            }
        }

        if (failures is not null)
        {
            throw new AggregateException("One or more shader node definitions failed to dispose.", failures);
        }
    }

    internal sealed class Snapshot : IDisposable
    {
        private readonly IReadOnlyDictionary<string, ShaderNodeDefinition> m_definitions;
        private bool m_disposed;

        private Snapshot(IReadOnlyDictionary<string, ShaderNodeDefinition> definitions)
        {
            m_definitions = definitions;
        }

        internal IReadOnlyDictionary<string, ShaderNodeDefinition> definitions
        {
            get
            {
                ObjectDisposedException.ThrowIf(m_disposed, this);
                return m_definitions;
            }
        }

        internal static Snapshot Create(IEnumerable<ShaderNodeDefinition> definitions)
        {
            ArgumentNullException.ThrowIfNull(definitions);
            var candidate = new SortedDictionary<string, ShaderNodeDefinition>(StringComparer.Ordinal);
            foreach (ShaderNodeDefinition definition in definitions)
            {
                ArgumentNullException.ThrowIfNull(definition);
                if (!candidate.TryAdd(definition.id, definition))
                {
                    throw new ArgumentException(
                        $"Shader node definition '{definition.id}' is duplicated.",
                        nameof(definitions));
                }
            }

            return new Snapshot(candidate);
        }

        /// <summary>
        /// Releases the resources owned by this instance.
        /// </summary>
        public void Dispose()
        {
            if (m_disposed)
            {
                return;
            }

            m_disposed = true;
            DisposeDefinitions(m_definitions.Values);
        }
    }
}
