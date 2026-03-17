namespace Inno.Core.Storage;

internal sealed class DependencyEntry<TValue>
{
    public TValue? value;
    public bool hasValue;
    public bool dirty = true;
    public int generation;
    public long lastAccessTicks;
    public long lastUpdateTicks;
}