using Photobooth.Core;
using Photobooth.UI.Services;

namespace Photobooth.UI.Tests;

public class CaptureModeOverrideSettingsProviderTests
{
    private static BoothSettings BaseSettings(string mode = "Photo") => new(
        CountdownSeconds: 3,
        GlamFilterEnabled: false,
        PrintTemplate: PrintTemplate.Default)
    {
        Capture = new CaptureSettings(Mode: mode),
    };

    [Fact]
    public async Task GetSettingsAsync_NoOverride_PassesThroughUnmodified()
    {
        var inner = new FakeSettingsProvider(BaseSettings("Photo"));
        var provider = new CaptureModeOverrideSettingsProvider(inner);

        BoothSettings result = await provider.GetSettingsAsync();

        Assert.Equal("Photo", result.Capture.Mode);
        Assert.Same(inner.Settings, result);
    }

    [Fact]
    public async Task GetSettingsAsync_OverridePresent_ReflectsOverriddenMode()
    {
        var inner = new FakeSettingsProvider(BaseSettings("Photo"));
        var provider = new CaptureModeOverrideSettingsProvider(inner) { Mode = "GIF" };

        BoothSettings result = await provider.GetSettingsAsync();

        Assert.Equal("GIF", result.Capture.Mode);
    }

    [Fact]
    public async Task GetSettingsAsync_OverrideMatchesConfiguredMode_ReturnsSameInstance()
    {
        // No `with` needed when the override already matches -- not required
        // behavior, just confirms the no-op path doesn't allocate a new record.
        var inner = new FakeSettingsProvider(BaseSettings("Boomerang"));
        var provider = new CaptureModeOverrideSettingsProvider(inner) { Mode = "Boomerang" };

        BoothSettings result = await provider.GetSettingsAsync();

        Assert.Same(inner.Settings, result);
    }

    [Fact]
    public async Task GetSettingsAsync_OverrideLeavesOtherSettingsUntouched()
    {
        var inner = new FakeSettingsProvider(BaseSettings("Photo"));
        var provider = new CaptureModeOverrideSettingsProvider(inner) { Mode = "Video" };

        BoothSettings result = await provider.GetSettingsAsync();

        Assert.Equal(inner.Settings.CountdownSeconds, result.CountdownSeconds);
        Assert.Equal(inner.Settings.GlamFilterEnabled, result.GlamFilterEnabled);
    }

    private class FakeSettingsProvider : IBoothSettingsProvider
    {
        public FakeSettingsProvider(BoothSettings settings) => Settings = settings;

        public BoothSettings Settings { get; }

        public Task<BoothSettings> GetSettingsAsync(CancellationToken ct = default) => Task.FromResult(Settings);
    }
}
