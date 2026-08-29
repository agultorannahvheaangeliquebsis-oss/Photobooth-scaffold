using Photobooth.Core;

namespace Photobooth.Tests;

public class QrCodeGeneratorTests
{
    [Fact]
    public void GeneratePng_ReturnsValidPngBytes()
    {
        byte[] png = QrCodeGenerator.GeneratePng("https://example.invalid/photo/123");

        byte[] pngSignature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
        Assert.True(png.Length > pngSignature.Length);
        Assert.Equal(pngSignature, png[..pngSignature.Length]);
    }
}
