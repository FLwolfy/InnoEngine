using Inno.Core.Storage;

namespace Inno.Rendering;

internal sealed class RenderPassGraphCompiler
{
    private readonly DependencyGraph<string, RenderPass> m_graph = new()
    {
        allowCycles = false,
        dependencyCacheMode = DependencyCacheMode.Disabled
    };
    private readonly RenderGraphResourcePlanner m_resourcePlanner = new();

    private readonly Dictionary<string, RenderPass> m_passByNode = new(StringComparer.Ordinal);
    private CompiledRenderPassGraph? m_cached;
    private int m_cachedSignature;

    public CompiledRenderPassGraph Compile(IReadOnlyList<RenderPass> passes)
    {
        ArgumentNullException.ThrowIfNull(passes);

        var signature = ComputeSignature(passes);
        if (m_cached is not null && signature == m_cachedSignature)
        {
            return m_cached;
        }

        var sorted = passes
            .OrderBy(static p => (int)p.passEvent)
            .ThenBy(static p => p.name, StringComparer.Ordinal)
            .ToArray();
        var declarations = BuildDeclarations(sorted);

        RebuildGraph(sorted, declarations);
        var topological = m_graph.TopologicalSort(out var cyclicNodes);
        if (cyclicNodes.Count > 0)
        {
            throw new InvalidOperationException("Render pass graph contains cyclic dependencies.");
        }

        var ordered = new List<RenderPass>(topological.Count);
        var declarationByPass = new Dictionary<RenderPass, RenderGraphPassDeclaration>(ReferenceEqualityComparer.Instance);
        foreach (var node in topological)
        {
            if (m_passByNode.TryGetValue(node, out var pass))
            {
                ordered.Add(pass);
                declarationByPass[pass] = BuildDeclaration(pass);
            }
        }

        m_cachedSignature = signature;
        m_cached = new CompiledRenderPassGraph
        {
            orderedPasses = ordered,
            passDeclarations = declarations,
            resourcePlan = m_resourcePlanner.Plan(declarations),
            declarationByPass = declarationByPass
        };
        return m_cached;
    }

    private void RebuildGraph(IReadOnlyList<RenderPass> sortedPasses, IReadOnlyList<RenderGraphPassDeclaration> declarations)
    {
        m_graph.Clear();
        m_passByNode.Clear();

        var nodes = new string[sortedPasses.Count];
        for (var index = 0; index < sortedPasses.Count; index++)
        {
            var pass = sortedPasses[index];
            var node = BuildNodeName(index, pass);
            nodes[index] = node;

            m_graph.AddNode(node);
            m_passByNode[node] = pass;
        }

        for (var index = 0; index < sortedPasses.Count; index++)
        {
            var currentNode = nodes[index];
            var currentDeclaration = declarations[index];
            for (var previousIndex = 0; previousIndex < index; previousIndex++)
            {
                var previousDeclaration = declarations[previousIndex];
                if (HasResourceDependency(previousDeclaration, currentDeclaration))
                {
                    m_graph.AddDependency(currentNode, nodes[previousIndex]);
                }
            }
        }
    }

    private static bool HasResourceDependency(RenderGraphPassDeclaration previous, RenderGraphPassDeclaration current)
    {
        foreach (var previousUsage in previous.resources)
        {
            foreach (var currentUsage in current.resources)
            {
                if (!string.Equals(previousUsage.name, currentUsage.name, StringComparison.Ordinal))
                {
                    continue;
                }

                if (IsWrite(previousUsage.access) || IsWrite(currentUsage.access))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool IsWrite(RenderGraphResourceAccess access)
    {
        return access is RenderGraphResourceAccess.Write or RenderGraphResourceAccess.ReadWrite;
    }

    private static string BuildNodeName(int index, RenderPass pass)
    {
        return $"{index:D3}_{(int)pass.passEvent}_{pass.name}";
    }

    private static int ComputeSignature(IReadOnlyList<RenderPass> passes)
    {
        var hash = new HashCode();
        hash.Add(passes.Count);

        foreach (var pass in passes)
        {
            hash.Add(pass.name, StringComparer.Ordinal);
            hash.Add((int)pass.passEvent);
            hash.Add(pass.GetType());
            var builder = new RenderGraphPassBuilder();
            pass.Setup(builder);
            var declaration = builder.Build();
            hash.Add(declaration.resources.Count);
            foreach (var usage in declaration.resources)
            {
                hash.Add(usage.name, StringComparer.Ordinal);
                hash.Add((int)usage.access);
                if (usage.descriptor is not null)
                {
                    hash.Add(usage.descriptor.size.width);
                    hash.Add(usage.descriptor.size.height);
                    hash.Add((int)usage.descriptor.colorFormat);
                    hash.Add(usage.descriptor.hasDepth);
                    hash.Add(usage.descriptor.hasMipmaps);
                }
            }
        }

        return hash.ToHashCode();
    }

    private static IReadOnlyList<RenderGraphPassDeclaration> BuildDeclarations(IReadOnlyList<RenderPass> passes)
    {
        var declarations = new RenderGraphPassDeclaration[passes.Count];
        for (var i = 0; i < passes.Count; i++)
        {
            var builder = new RenderGraphPassBuilder();
            passes[i].Setup(builder);
            declarations[i] = builder.Build();
        }

        return declarations;
    }

    private static RenderGraphPassDeclaration BuildDeclaration(RenderPass pass)
    {
        var builder = new RenderGraphPassBuilder();
        pass.Setup(builder);
        return builder.Build();
    }

    private sealed class ReferenceEqualityComparer : IEqualityComparer<RenderPass>
    {
        public static ReferenceEqualityComparer Instance { get; } = new();

        public bool Equals(RenderPass? x, RenderPass? y) => ReferenceEquals(x, y);

        public int GetHashCode(RenderPass obj) => System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
    }
}
