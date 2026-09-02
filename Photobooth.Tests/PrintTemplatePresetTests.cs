using Photobooth.Core;

namespace Photobooth.Tests;

public class PrintTemplateLibraryFieldsTests
{
    [Fact]
    public void Default_HasNoLibraryIdentity()
    {
        Assert.Equal(0, PrintTemplate.Default.Id);
        Assert.False(PrintTemplate.Default.IsFavorite);
    }

    [Fact]
    public void With_OverridesLibraryFields_WithoutTouchingExistingConstructorCallSites()
    {
        PrintTemplate template = new("Single", 4, 6, 1) { Id = 7, Name = "Birthday Bash", IsFavorite = true };

        Assert.Equal(7, template.Id);
        Assert.Equal("Birthday Bash", template.Name);
        Assert.True(template.IsFavorite);
        Assert.Equal("Single", template.Layout);
    }
}

public class PrintTemplatePresetTests
{
    [Fact]
    public void Blank_HasNoElementsAndRequiresOnePhoto()
    {
        Assert.Empty(PrintTemplatePresets.Blank.Elements);
        Assert.Equal(1, PrintTemplatePresets.Blank.RequiredPhotoCount);
    }

    [Fact]
    public void FourPosesGrid_RequiresExactlyFourPhotos()
    {
        Assert.Equal(4, PrintTemplatePresets.FourPosesGrid.RequiredPhotoCount);
    }

    /// <summary>The double-strip preset draws 8 PhotoSlot elements (4 per column) but
    /// reuses PhotoIndex 0-3 across both columns so the same 4 captured poses are
    /// drawn twice -- RequiredPhotoCount must still read 4, not 8, or
    /// BoothStateMachine would ask the guest to pose for 8 distinct shots.</summary>
    [Fact]
    public void FourPosesDoubleStrip_StillRequiresOnlyFourPhotosDespiteEightSlots()
    {
        Assert.Equal(8, PrintTemplatePresets.FourPosesDoubleStrip.Elements.Count);
        Assert.Equal(4, PrintTemplatePresets.FourPosesDoubleStrip.RequiredPhotoCount);
    }

    [Fact]
    public void SinglePoseRepeatedStrip_HasNoPhotoSlotElements_SoStripCopiesHandlesTheRepeat()
    {
        Assert.Empty(PrintTemplatePresets.SinglePoseRepeatedStrip.Elements);
        Assert.Equal(1, PrintTemplatePresets.SinglePoseRepeatedStrip.RequiredPhotoCount);
        Assert.Equal(2, PrintTemplatePresets.SinglePoseRepeatedStrip.StripCopies);
    }

    [Fact]
    public void All_EveryPresetElementIsValid()
    {
        foreach (PrintTemplatePreset preset in PrintTemplatePresets.All)
        {
            foreach (PrintTemplateElement element in preset.Elements)
            {
                Assert.True(element.IsValid, $"{preset.Id} has an invalid element ({element.Kind}).");
            }
        }
    }

    [Fact]
    public void All_EveryPresetProducesAValidPrintTemplate()
    {
        foreach (PrintTemplatePreset preset in PrintTemplatePresets.All)
        {
            var template = new PrintTemplate(preset.Layout, preset.WidthInches, preset.HeightInches, preset.StripCopies)
            {
                Elements = preset.Elements,
            };
            Assert.True(template.IsValid, $"{preset.Id} produces an invalid PrintTemplate.");
        }
    }
}
