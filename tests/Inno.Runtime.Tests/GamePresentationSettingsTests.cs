using System;
using System.IO;

using Inno.Core.Settings;
using Inno.Runtime;
using Xunit;

namespace Inno.Runtime.Tests;

public sealed class GamePresentationSettingsTests
{
    [Fact]
    public void DefaultPresentationCentersCompleteSixteenByNineFrame()
    {
        var settings = new GamePresentationSettings();

        GamePresentationViewport viewport = settings.CalculateViewport(1000, 1000);

        Assert.Equal(new GamePresentationViewport(0, 219, 1000, 562), viewport);
    }

    [Fact]
    public void StretchPresentationUsesCompleteAvailableSurface()
    {
        var settings = new GamePresentationSettings
        {
            preserveAspectRatio = false,
            referenceWidth = 4,
            referenceHeight = 3
        };

        GamePresentationViewport viewport = settings.CalculateViewport(1000, 400);

        Assert.Equal(new GamePresentationViewport(0, 0, 1000, 400), viewport);
    }

    [Fact]
    public void PresentationRejectsInvalidReferenceResolution()
    {
        var settings = new GamePresentationSettings { referenceWidth = 0 };

        Assert.Throws<ArgumentOutOfRangeException>(() => settings.CalculateViewport(1000, 1000));
    }

    [Fact]
    public void RuntimeHostRegistersDefaultPresentationProjectSetting()
    {
        string directory = Path.Combine(
            Path.GetTempPath(),
            "InnoGamePresentationTests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            using EngineHost host = new EngineHostBuilder()
                .UseMetadataCache(Path.Combine(directory, "Metadata"))
                .Build();
            using var settings = new ProjectSettingsStore(
                Path.Combine(directory, "ProjectSettings.inno"),
                host.types,
                host.serialization);
            settings.RebuildCurrent();

            GamePresentationSettings presentation = settings.Get<GamePresentationSettings>(
                GamePresentationSettings.settingId);

            Assert.True(presentation.preserveAspectRatio);
            Assert.Equal(1280, presentation.referenceWidth);
            Assert.Equal(720, presentation.referenceHeight);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
