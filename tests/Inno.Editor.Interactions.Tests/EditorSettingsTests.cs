using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Inno.Extensibility.Modules;
using Inno.Extensibility.Types;
using Inno.Core.Diagnostics;
using Inno.Core.Logging;
using Inno.Core.Serialization;
using Inno.Editor.Core;
using Inno.Editor.Diagnostics;
using Inno.Editor.Interactions;
using Inno.Editor.PlayMode;
using Inno.Editor.Settings;
using Xunit;

namespace Inno.Editor.Interactions.Tests;

public sealed class EditorSettingsTests : IDisposable
{
    private readonly string m_projectRoot = Path.Combine(
        Path.GetTempPath(),
        "InnoEditorSettingsTests",
        Guid.NewGuid().ToString("N"));
    private readonly EditorInteractionRuntime m_runtime;
    private readonly EditorSettings m_settings;
    private readonly IEditorConsole m_console;
    private readonly ModuleHost m_modules;
    private readonly TypeCatalog m_types;
    private readonly SerializationRegistry m_serialization;
    private readonly DiagnosticHub m_diagnostics = new();
    private readonly FakePlayMode m_playMode = new();
    private readonly LogRouter m_logs = new();

    public EditorSettingsTests()
    {
        Directory.CreateDirectory(m_projectRoot);
        m_modules = new ModuleHost(new ModuleHostOptions
        {
            cacheDirectory = Path.Combine(m_projectRoot, "Library", "Assemblies")
        });
        _ = typeof(EditorSettings);
        _ = System.Reflection.Assembly.Load("Inno.Editor.Panel.Logging");
        m_types = new TypeCatalog(m_modules);
        m_serialization = new SerializationRegistry(m_types);
        SettingsCaptureModule.current = null;
        SettingsCaptureModule.console = null;
        m_runtime = new EditorInteractionRuntime(
            new EditorContext(m_projectRoot),
            m_types,
            m_logs,
            [m_types, m_serialization, m_diagnostics, m_playMode]);
        m_runtime.Start();
        m_settings = SettingsCaptureModule.current
            ?? throw new InvalidOperationException("The Settings module was not discovered.");
        m_console = SettingsCaptureModule.console
            ?? throw new InvalidOperationException("The Console module was not discovered.");
    }

    public void Dispose()
    {
        m_runtime.Dispose();
        SettingsCaptureModule.current = null;
        SettingsCaptureModule.console = null;
        m_serialization.Dispose();
        m_types.Dispose();
        m_modules.Dispose();
        m_logs.Dispose();
        if (Directory.Exists(m_projectRoot))
            Directory.Delete(m_projectRoot, recursive: true);
    }

    [Fact]
    public void ApplyPersistsObjectsAtTheProjectRootAndPublishesOnlyTheService()
    {
        int changedCount = 0;
        EditorSettings? published = null;
        m_settings.changed += settings =>
        {
            changedCount++;
            published = settings;
        };
        var value = new EditorSettingObject();
        value.SetAsInt32("value", 10);

        Assert.True(m_settings.Apply(
            new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal)
            {
                ["Editor/Tests/Values/Project Count"] = value
            }));

        string path = Path.Combine(m_projectRoot, "EditorSettings.inno");
        Assert.True(File.Exists(path));
        Assert.False(Directory.Exists(Path.Combine(m_projectRoot, "Settings")));
        Assert.Equal(10, m_settings.Get("Editor/Tests/Values/Project Count").GetAsInt32("value"));
        Assert.Equal(1, changedCount);
        Assert.Same(m_settings, published);
    }

    [Fact]
    public void ChangedSubscriberFailureDoesNotChangeHistoryResultOrSkipLaterSubscribers()
    {
        int delivered = 0;
        m_settings.changed += _ => throw new InvalidOperationException("subscriber");
        m_settings.changed += _ => delivered++;
        var value = new EditorSettingObject();
        value.SetAsInt32("value", 11);

        Assert.True(m_settings.Apply(
            new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal)
            {
                ["Editor/Tests/Values/Project Count"] = value
            }));
        Assert.Equal(1, delivered);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(2, delivered);
        Assert.Equal(3, m_settings.Get("Editor/Tests/Values/Project Count").GetAsInt32("value"));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(3, delivered);
        Assert.Equal(11, m_settings.Get("Editor/Tests/Values/Project Count").GetAsInt32("value"));
    }

    [Fact]
    public void GetReturnsIsolatedObjectsAndRejectsPagesOrMissingPaths()
    {
        EditorSettingObject first = m_settings.Get("Editor/Tests/Values/Project Count");
        first.SetAsInt32("value", 99);

        Assert.Equal(3, m_settings.Get("Editor/Tests/Values/Project Count").GetAsInt32("value"));
        Assert.Throws<ArgumentException>(() => m_settings.Get("Editor/Tests"));
        Assert.Throws<ArgumentException>(() => m_settings.Get("Editor/Tests/Missing"));
    }

    [Fact]
    public void ResetRemovesThePathOverrideAndRestoresTheDefinitionObject()
    {
        EditorSetting definition = Assert.Single(
            m_settings.definitions,
            static setting => setting.path == "Editor/Tests/Values/Project Count");
        var value = new EditorSettingObject();
        value.SetAsInt32("value", 8);
        Assert.True(m_settings.Apply(
            new Dictionary<string, EditorSettingObject>
            {
                ["Editor/Tests/Values/Project Count"] = value
            }));
        Assert.False(definition.IsDefault(m_settings.Get("Editor/Tests/Values/Project Count")));

        Assert.True(m_settings.Apply(
            new Dictionary<string, EditorSettingObject>(),
            new HashSet<string>(StringComparer.Ordinal)
            {
                "Editor/Tests/Values/Project Count"
            }));

        Assert.Equal(3, m_settings.Get("Editor/Tests/Values/Project Count").GetAsInt32("value"));
        Assert.True(definition.IsDefault(m_settings.Get("Editor/Tests/Values/Project Count")));
        Assert.NotSame(definition.defaultValue, definition.defaultValue);
        Assert.NotEmpty(File.ReadAllBytes(Path.Combine(m_projectRoot, "EditorSettings.inno")));
    }

    [Fact]
    public void OneApplyCreatesOneSharedHistoryEntryForAllSettings()
    {
        var count = new EditorSettingObject();
        count.SetAsInt32("value", 7);
        var toggle = new EditorSettingObject();
        toggle.SetAsBoolean("value", false);

        Assert.True(m_settings.Apply(
            new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal)
            {
                ["Editor/Tests/Values/Project Count"] = count,
                ["Editor/Tests/Values/Project Toggle"] = toggle
            }));
        Assert.Equal("Apply Settings", m_runtime.interactions.history.undoName);
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.Equal(3, m_settings.Get("Editor/Tests/Values/Project Count").GetAsInt32("value"));
        Assert.True(m_settings.Get("Editor/Tests/Values/Project Toggle").GetAsBoolean("value"));
        Assert.True(m_runtime.interactions.history.Redo().succeeded);
        Assert.Equal(7, m_settings.Get("Editor/Tests/Values/Project Count").GetAsInt32("value"));
        Assert.False(m_settings.Get("Editor/Tests/Values/Project Toggle").GetAsBoolean("value"));
    }

    [Fact]
    public void ClearOnPlaySettingDefaultsToTrueAndImmediatelyControlsTheConsole()
    {
        const string path = "Editor/Diagnostics/Console/Clear on Play";
        Assert.True(m_settings.Get(path).GetAsBoolean("value"));
        Assert.True(m_console.clearOnPlay);

        var disabled = new EditorSettingObject();
        disabled.SetAsBoolean("value", false);
        Assert.True(m_settings.Apply(
            new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal)
            {
                [path] = disabled
            }));
        Assert.False(m_console.clearOnPlay);

        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(m_console.clearOnPlay);
    }

    [Fact]
    public void CatalogKeepsRawStringPathsAndAlphabeticalSections()
    {
        EditorSetting page = Assert.Single(
            m_settings.definitions,
            static definition => definition.path == "Editor/Tests");
        Assert.False(page.hasValue);
        Assert.Equal("Test-only settings overview.", page.description);
        Assert.Equal(
            new[] { "Alpha", "Numbers" },
            m_settings.definitions
                .Where(static definition => definition.pagePath == "Editor/Tests/Values")
                .OrderBy(static definition => definition.section, StringComparer.Ordinal)
                .Select(static definition => definition.section));
    }

    [Fact]
    public void SettingObjectRoundTripsSupportedPrimitivesAndArrays()
    {
        var value = new EditorSettingObject();
        value.SetAsBoolean("boolean", true);
        value.SetAsInt32("int32", -32);
        value.SetAsUInt32("uint32", 32u);
        value.SetAsInt64("int64", -64L);
        value.SetAsUInt64("uint64", 64UL);
        value.SetAsSingle("single", 1.25f);
        value.SetAsDouble("double", 2.5d);
        value.SetAsString("string", "value");
        value.SetAsBooleanArray("booleans", [true, false]);
        value.SetAsInt32Array("int32s", [-1, 2]);
        value.SetAsUInt32Array("uint32s", [1u, 2u]);
        value.SetAsSingleArray("singles", [1.5f, 2.5f]);
        value.SetAsDoubleArray("doubles", [3.5d, 4.5d]);
        value.SetAsStringArray("strings", ["first", null, "third"]);

        Assert.True(m_settings.Apply(
            new Dictionary<string, EditorSettingObject>(StringComparer.Ordinal)
            {
                ["Editor/Tests/Objects/Primitives"] = value
            }));
        Assert.True(m_runtime.interactions.history.Undo().succeeded);
        Assert.True(m_runtime.interactions.history.Redo().succeeded);

        EditorSettingObject restored = m_settings.Get("Editor/Tests/Objects/Primitives");
        Assert.True(restored.GetAsBoolean("boolean"));
        Assert.Equal(-32, restored.GetAsInt32("int32"));
        Assert.Equal(32u, restored.GetAsUInt32("uint32"));
        Assert.Equal(-64L, restored.GetAsInt64("int64"));
        Assert.Equal(64UL, restored.GetAsUInt64("uint64"));
        Assert.Equal(1.25f, restored.GetAsSingle("single"));
        Assert.Equal(2.5d, restored.GetAsDouble("double"));
        Assert.Equal("value", restored.GetAsString("string"));
        Assert.Equal(new[] { true, false }, restored.GetAsBooleanArray("booleans"));
        Assert.Equal(new[] { -1, 2 }, restored.GetAsInt32Array("int32s"));
        Assert.Equal(new[] { 1u, 2u }, restored.GetAsUInt32Array("uint32s"));
        Assert.Equal(new[] { 1.5f, 2.5f }, restored.GetAsSingleArray("singles"));
        Assert.Equal(new[] { 3.5d, 4.5d }, restored.GetAsDoubleArray("doubles"));
        Assert.Equal(
            new string?[] { "first", null, "third" },
            restored.GetAsStringArray("strings"));

        string?[] strings = restored.GetAsStringArray("strings");
        strings[0] = "mutated";
        Assert.Equal("first", restored.GetAsStringArray("strings")[0]);
        Assert.Throws<ArgumentOutOfRangeException>(
            () => restored.SetAsSingle("invalid", float.PositiveInfinity));
        Assert.Throws<ArgumentException>(() => restored.GetAsString(string.Empty));
    }

    [Fact]
    public void DrawTracksDifferenceFromEachStagedObjectsFirstDraw()
    {
        EditorSetting definition = Assert.Single(
            m_settings.definitions,
            static setting => setting.path == "Editor/Tests/Objects/Draw Mutation");
        var value = new EditorSettingObject();

        Assert.True(definition.Draw(value));
        Assert.Equal(42, value.GetAsInt32("value"));
        Assert.True(definition.Draw(value));

        var alreadyCommitted = new EditorSettingObject();
        alreadyCommitted.SetAsInt32("value", 42);
        Assert.False(definition.Draw(alreadyCommitted));
    }

    [EditorModule("tests.settings-capture", order: int.MaxValue)]
    private sealed class SettingsCaptureModule(
        EditorSettings settings,
        IEditorConsole editorConsole) : EditorModule
    {
        internal static EditorSettings? current;
        internal static IEditorConsole? console;

        protected override void OnStart(EditorContext context)
        {
            current = settings;
            console = editorConsole;
        }
    }

    private sealed class FakePlayMode : IEditorPlayMode
    {
        public EditorPlayModeState state => EditorPlayModeState.Editing;

        public bool isPlaying => false;

        public string? lastFailure => null;

        public LogSessionId activeSessionId => LogSessionId.none;

        public event Action<EditorPlayModeState>? stateChanged
        {
            add
            {
            }
            remove
            {
            }
        }

        public bool EnterPlayMode() => false;

        public bool ExitPlayMode() => false;
    }

    [EditorSettingPath("Editor/Tests")]
    private sealed class TestSettingsPage : EditorSetting
    {
        public override string description => "Test-only settings overview.";
    }

    [EditorSettingPath("Editor/Tests/Values/Project Count")]
    private sealed class TestCountSetting : EditorSetting
    {
        public override EditorSettingObject defaultValue => CreateDefault();

        public override string section => "Numbers";

        public override string description => "Exercises path-addressed project persistence.";

        protected override void OnDraw(EditorSettingObject setting)
        {
        }

        private static EditorSettingObject CreateDefault()
        {
            var result = new EditorSettingObject();
            result.SetAsInt32("value", 3);
            return result;
        }
    }

    [EditorSettingPath("Editor/Tests/Values/Project Toggle")]
    private sealed class TestToggleSetting : EditorSetting
    {
        public override EditorSettingObject defaultValue => CreateDefault();

        public override string section => "Alpha";

        protected override void OnDraw(EditorSettingObject setting)
        {
        }

        private static EditorSettingObject CreateDefault()
        {
            var result = new EditorSettingObject();
            result.SetAsBoolean("value", true);
            return result;
        }
    }

    [EditorSettingPath("Editor/Tests/Objects/Primitives")]
    private sealed class TestPrimitiveObjectSetting : EditorSetting
    {
        public override EditorSettingObject defaultValue => new();

        protected override void OnDraw(EditorSettingObject setting)
        {
        }
    }

    [EditorSettingPath("Editor/Tests/Objects/Draw Mutation")]
    private sealed class TestDrawMutationSetting : EditorSetting
    {
        public override EditorSettingObject defaultValue => new();

        protected override void OnDraw(EditorSettingObject setting)
            => setting.SetAsInt32("value", 42);
    }
}
