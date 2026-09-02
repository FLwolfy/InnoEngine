using System;
using System.Threading.Tasks;

using Inno.Core.Serialization;
using Inno.Core.Settings;
using RuntimeSettings = Inno.Core.Settings.Settings;

using Xunit;

namespace Inno.Editor.Scripting.Tests;

public sealed class ProjectSettingsExecutionContextTests
{
    private static readonly ProjectSettingId S_SETTING_ID = new("tests.execution-context");

    [Fact]
    public void Settings_WithoutAnExecutionScope_FailsExplicitly()
    {
        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() =>
            RuntimeSettings.Get<TestSetting>(S_SETTING_ID));

        Assert.Contains("No project settings are bound", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NestedScopes_RestoreThePreviousSettingsLookup()
    {
        var outer = new FixedSettingsLookup(11);
        var inner = new FixedSettingsLookup(22);

        using (ProjectSettingsExecutionContext.EnterScope(outer))
        {
            Assert.Equal(11, RuntimeSettings.Get<TestSetting>(S_SETTING_ID).value);
            using (ProjectSettingsExecutionContext.EnterScope(inner))
                Assert.Equal(22, RuntimeSettings.Get<TestSetting>(S_SETTING_ID).value);
            Assert.Equal(11, RuntimeSettings.Get<TestSetting>(S_SETTING_ID).value);
        }
    }

    [Fact]
    public void Scopes_DisposedOutOfOrder_AreRejectedWithoutLosingTheActiveLookup()
    {
        var outer = new FixedSettingsLookup(11);
        var inner = new FixedSettingsLookup(22);
        IDisposable outerScope = ProjectSettingsExecutionContext.EnterScope(outer);
        IDisposable innerScope = ProjectSettingsExecutionContext.EnterScope(inner);

        try
        {
            Assert.Throws<InvalidOperationException>(outerScope.Dispose);
            Assert.Same(inner, ProjectSettingsExecutionContext.current);
        }
        finally
        {
            innerScope.Dispose();
            outerScope.Dispose();
        }
    }

    [Fact]
    public async Task ParallelAsyncFlows_ResolveTheirOwnSettingsLookups()
    {
        Task<int> first = ResolveAsync(new FixedSettingsLookup(11));
        Task<int> second = ResolveAsync(new FixedSettingsLookup(22));

        Assert.Equal(11, await first);
        Assert.Equal(22, await second);
    }

    private static async Task<int> ResolveAsync(IProjectSettingsLookup lookup)
    {
        using IDisposable scope = ProjectSettingsExecutionContext.EnterScope(lookup);
        await Task.Yield();
        return RuntimeSettings.Get<TestSetting>(S_SETTING_ID).value;
    }

    private sealed class FixedSettingsLookup(int value) : IProjectSettingsLookup
    {
        public long revision => value;

        public TSetting Get<TSetting>(ProjectSettingId id)
            where TSetting : class, ISerializable
        {
            if (typeof(TSetting) != typeof(TestSetting))
                throw new InvalidOperationException("The requested test setting type is unsupported.");
            return (TSetting)(object)new TestSetting { value = value };
        }

        public bool TryGet<TSetting>(ProjectSettingId id, out TSetting? setting)
            where TSetting : class, ISerializable
        {
            setting = Get<TSetting>(id);
            return true;
        }
    }

    private sealed class TestSetting : ISerializable
    {
        [SerializableProperty]
        public int value { get; set; }
    }
}
