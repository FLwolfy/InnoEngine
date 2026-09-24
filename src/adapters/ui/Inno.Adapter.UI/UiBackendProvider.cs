using Inno.UI;

namespace Inno.Adapter.UI;

/// <summary>Creates one concrete UI backend registered by a composition root.</summary>
public abstract class UiBackendProvider
{
    /// <summary>Gets this provider's stable implementation identity.</summary>
    public abstract UiBackendId id { get; }

    /// <summary>Creates one caller-owned backend generation.</summary>
    /// <returns>A new backend.</returns>
    public abstract IUiBackend CreateBackend();
}
