using Photobooth.Core;
using Photobooth.UI.ViewModels;

namespace Photobooth.UI.Tests;

/// <summary>Direct coverage of KioskViewModel.MapScreen's BoothState -> KioskScreen
/// table -- the mapping decisions Day 1's screen ports changed (FramePicker/
/// Payment/Guestbook/Feedback/Survey each moved off the shared Processing/Review
/// screens onto their own). Exercised directly (via InternalsVisibleTo) rather
/// than by driving a full BoothStateMachine, since this is a pure switch with no
/// state to set up.</summary>
public class KioskViewModelMapScreenTests
{
    [Theory]
    [InlineData(BoothState.Setup, KioskScreen.Idle)]
    [InlineData(BoothState.Idle, KioskScreen.Idle)]
    [InlineData(BoothState.Countdown, KioskScreen.Countdown)]
    [InlineData(BoothState.Capturing, KioskScreen.Capture)]
    [InlineData(BoothState.Consent, KioskScreen.Processing)]
    [InlineData(BoothState.Reviewing, KioskScreen.Processing)]
    [InlineData(BoothState.Printing, KioskScreen.Processing)]
    [InlineData(BoothState.FilterPicker, KioskScreen.FilterPicker)]
    [InlineData(BoothState.FramePicker, KioskScreen.FramePicker)]
    [InlineData(BoothState.Payment, KioskScreen.Payment)]
    [InlineData(BoothState.Guestbook, KioskScreen.Guestbook)]
    [InlineData(BoothState.Feedback, KioskScreen.Feedback)]
    [InlineData(BoothState.Survey, KioskScreen.Survey)]
    [InlineData(BoothState.Complete, KioskScreen.Review)]
    [InlineData(BoothState.Error, KioskScreen.Error)]
    public void MapScreen_ReturnsExpectedScreen(BoothState state, KioskScreen expected)
    {
        Assert.Equal(expected, KioskViewModel.MapScreen(state));
    }

    [Fact]
    public void MapScreen_OnlySetupAndIdleMapToIdleScreen()
    {
        // Guards against a new/unmapped BoothState value silently falling
        // through to MapScreen's `_ => KioskScreen.Idle` default instead of
        // an explicit, reviewed mapping above.
        var unexpectedlyIdle = Enum.GetValues<BoothState>()
            .Where(state => state is not (BoothState.Setup or BoothState.Idle))
            .Where(state => KioskViewModel.MapScreen(state) == KioskScreen.Idle)
            .ToList();

        Assert.Empty(unexpectedlyIdle);
    }
}
