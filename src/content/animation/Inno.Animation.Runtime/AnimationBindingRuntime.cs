using System;
using System.Collections.Generic;
using System.Reflection;
using Inno.Core.Diagnostics;
using Inno.Extensibility.Types;

namespace Inno.Animation.Runtime;

/// <summary>
/// Resolves declared binding protocols through transactional, generation-owned extension snapshots.
/// </summary>
public sealed class AnimationBindingRuntime : IAnimationBindingSink, IDisposable
{
    private readonly Registry m_registry;
    private readonly IDiagnosticReporter m_diagnostics;
    private readonly int m_ownerThread = Environment.CurrentManagedThreadId;
    private bool m_disposed;

    /// <summary>
    /// Creates a binding owner connected to the host's transactional type catalog.
    /// </summary>
    /// <param name="types">
    /// The shared type catalog; candidate failure preserves its last good snapshot.
    /// </param>
    /// <param name="diagnostics">
    /// A borrowed reporter scoped to this session's binding diagnostics.
    /// </param>
    public AnimationBindingRuntime(TypeCatalog types, IDiagnosticReporter diagnostics)
    {
        ArgumentNullException.ThrowIfNull(types);
        m_diagnostics = diagnostics ?? throw new ArgumentNullException(nameof(diagnostics));
        m_registry = new Registry(types);
        try { m_registry.Refresh(); }
        catch { m_registry.Dispose(); throw; }
    }

    /// <summary>
    /// Applies a frame batch using the currently committed binding generation.
    /// </summary>
    /// <param name="samples">
    /// Final samples, independently blended by destination and binding.
    /// </param>
    /// <exception cref="ObjectDisposedException">
    /// This binding owner has retired.
    /// </exception>
    /// <exception cref="InvalidOperationException">
    /// Called outside the owner thread.
    /// </exception>
    public void Apply(ReadOnlySpan<AnimationSample> samples)
    {
        ObjectDisposedException.ThrowIf(m_disposed, this);
        if (Environment.CurrentManagedThreadId != m_ownerThread)
            throw new InvalidOperationException("Animation bindings must run on their owner thread.");
        Snapshot snapshot = m_registry.snapshot;
        var issues = new Dictionary<(string, Guid), Diagnostic>();
        foreach (AnimationSample sample in samples)
        {
            if (sample.target.isSamplingOnly)
                continue;
            string? message = null;
            if (!snapshot.providers.TryGetValue((sample.binding, sample.value.kind), out AnimationBindingProvider? provider))
                message = "The animation binding provider is missing; the playback remains available.";
            else
            {
                try
                {
                    if (!provider.TryApply(sample.target, sample.value))
                        message = "The animation destination is missing or incompatible.";
                }
                catch (Exception exception)
                {
                    message = $"The animation binding failed: {exception.Message}";
                }
            }
            if (message is not null)
                issues[(sample.binding.value, sample.target.persistentId)] = new Diagnostic(
                    "animation.binding.unavailable", message, DiagnosticSeverity.Warning,
                    sample.binding.value, sample.target.persistentId);
        }
        m_diagnostics.Replace(issues.Values);
    }

    /// <summary>
    /// Releases all provider instances and unregisters their generation participant.
    /// </summary>
    public void Dispose()
    {
        if (m_disposed)
            return;
        m_disposed = true;
        m_registry.Dispose();
        m_diagnostics.Replace([]);
    }

    private sealed class Registry(TypeCatalog types) : TypeRegistry<Snapshot>(types)
    {
        internal Snapshot snapshot => current;

        /// <summary>
        /// Validates and constructs every binding before publication.
        /// </summary>
        /// <param name="types">
        /// The candidate type generation.
        /// </param>
        /// <returns>
        /// A complete binding plan owned by the candidate generation.
        /// </returns>
        protected override Snapshot Build(TypeCacheSnapshot types)
        {
            var result = new Snapshot();
            var ids = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                foreach (TypeRef reference in types.GetTypesWithAttribute<AnimationBindingProviderAttribute>())
                {
                    Type type = reference.Resolve(types);
                    AnimationBindingProviderAttribute declaration = type.GetCustomAttribute<AnimationBindingProviderAttribute>(false)!;
                    if (type.IsAbstract || type.ContainsGenericParameters || !typeof(AnimationBindingProvider).IsAssignableFrom(type)
                        || type.GetConstructor(Type.EmptyTypes) is null)
                        throw new InvalidOperationException($"Animation provider '{declaration.id}' requires a concrete public parameterless implementation.");
                    var key = (declaration.bindingId, declaration.kind);
                    if (!ids.Add(declaration.id) || result.providers.ContainsKey(key))
                        throw new InvalidOperationException($"Animation provider '{declaration.id}' duplicates an extension ID or binding protocol.");
                    result.providers.Add(key, (AnimationBindingProvider)Activator.CreateInstance(type)!);
                }
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Releases a rejected or retired binding plan.
        /// </summary>
        /// <param name="snapshot">
        /// The generation that is no longer active.
        /// </param>
        protected override void DisposeSnapshot(Snapshot snapshot) => snapshot.Dispose();
    }

    private sealed class Snapshot : IDisposable
    {
        internal Dictionary<(AnimationBindingId, AnimationValueKind), AnimationBindingProvider> providers { get; } = [];

        /// <summary>
        /// Attempts to release every provider without retaining failed instances.
        /// </summary>
        public void Dispose()
        {
            List<Exception>? failures = null;
            foreach (AnimationBindingProvider provider in providers.Values)
            {
                try { provider.Dispose(); }
                catch (Exception exception) { (failures ??= []).Add(exception); }
            }
            providers.Clear();
            if (failures is not null)
                throw new AggregateException("Animation provider retirement failed.", failures);
        }
    }
}
