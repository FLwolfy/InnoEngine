namespace Inno.Rendering;

internal sealed class RenderGraphResourcePlanner
{
    public CompiledRenderGraphResourcePlan Plan(IReadOnlyList<RenderGraphPassDeclaration> declarations)
    {
        ArgumentNullException.ThrowIfNull(declarations);
        if (declarations.Count == 0)
        {
            return CompiledRenderGraphResourcePlan.EMPTY;
        }

        var lifetimes = new Dictionary<string, MutableLifetime>(StringComparer.Ordinal);
        for (var passIndex = 0; passIndex < declarations.Count; passIndex++)
        {
            var declaration = declarations[passIndex];
            foreach (var usage in declaration.resources)
            {
                if (!lifetimes.TryGetValue(usage.name, out var lifetime))
                {
                    lifetime = new MutableLifetime(passIndex);
                    lifetimes.Add(usage.name, lifetime);
                }

                lifetime.lastPassIndex = passIndex;

                switch (usage.access)
                {
                    case RenderGraphResourceAccess.Read:
                        break;
                    case RenderGraphResourceAccess.Write:
                    case RenderGraphResourceAccess.ReadWrite:
                        if (usage.descriptor is not null)
                        {
                            lifetime.descriptor ??= usage.descriptor;
                        }

                        lifetime.hasWriter = true;
                        break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        var resources = new List<CompiledRenderGraphResource>(lifetimes.Count);
        foreach (var pair in lifetimes)
        {
            var life = pair.Value;
            resources.Add(new CompiledRenderGraphResource
            {
                name = pair.Key,
                firstPassIndex = life.firstPassIndex,
                lastPassIndex = life.lastPassIndex,
                isExternal = !life.hasWriter || life.descriptor is null,
                descriptor = life.descriptor
            });
        }

        resources.Sort(static (a, b) => string.CompareOrdinal(a.name, b.name));
        return new CompiledRenderGraphResourcePlan
        {
            resources = resources
        };
    }

    private sealed class MutableLifetime
    {
        public MutableLifetime(int index)
        {
            firstPassIndex = index;
            lastPassIndex = index;
        }

        public int firstPassIndex;
        public int lastPassIndex;
        public bool hasWriter;
        public RenderTargetDescriptor? descriptor;
    }
}
