using System;
using System.Collections.Generic;

using Inno.Rendering;

namespace Inno.Rendering.Runtime;

/// <summary>
/// Tracks successfully initialized presentation targets while ordered model contributions are composed.
/// </summary>
internal sealed class RenderPresentationComposer
{
    private readonly List<PresentationRegion> m_initializedRegions = [];

    internal bool MustPreserve(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return m_initializedRegions.Exists(region =>
            region.target == request.target
            && Overlaps(region.viewport, request.viewport));
    }

    internal void Commit(RenderRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        m_initializedRegions.Add(new PresentationRegion(request.target, request.viewport));
    }

    private static bool Overlaps(RenderViewport left, RenderViewport right)
        => (long)left.x < (long)right.x + right.width
           && (long)right.x < (long)left.x + left.width
           && (long)left.y < (long)right.y + right.height
           && (long)right.y < (long)left.y + left.height;

    private readonly record struct PresentationRegion(
        RenderTarget target,
        RenderViewport viewport);
}
