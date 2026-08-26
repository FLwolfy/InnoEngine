namespace ProjectPluginApi;

public static class PluginValue
{
    public const int value = 42;
}

public sealed class PluginObject
{
    public int value => PluginValue.value;
}
