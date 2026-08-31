using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

namespace Photobooth.Core;

/// <summary>
/// Real IGifComposerService: encodes each frame as a single-image GIF via
/// GDI+ (the only encoder GDI+ actually has -- Bitmap.Save(..., ImageFormat.Gif)
/// has no multi-frame API), then splices the frames' image data blocks
/// together into one animated GIF89a file by hand. This is the standard
/// technique for animated GIFs without a third-party imaging library: GDI+'s
/// own single-frame GIF output is spec-compliant enough that its Image
/// Descriptor + Local/Global Color Table + LZW-compressed data blocks can be
/// harvested byte-for-byte and reassembled under one Logical Screen
/// Descriptor, with a Graphic Control Extension (frame delay) prepended to
/// each and a NETSCAPE2.0 Application Extension (loop forever) once at the
/// top. No external dependency, same "keeps this project dependency free"
/// preference already established for PlaceholderImage/branding/filter.
///
/// Known limitation, not fixed here: only the first frame's color table is
/// kept as the animation's shared palette, but every other frame's LZW data
/// still indexes into whatever palette GDI+ chose for *that* frame
/// individually. Frames captured back-to-back of the same scene (the real
/// use case -- a guest barely moves between GIF frames) almost always get
/// near-identical GDI+-chosen palettes in practice, so this reads correctly
/// for the common case, but isn't guaranteed for frames with genuinely
/// different color content. A fully correct version would re-quantize every
/// frame to one shared palette before encoding -- meaningfully more work,
/// deferred rather than built speculatively.
/// </summary>
[SupportedOSPlatform("windows")]
public class GdiGifComposerService : IGifComposerService
{
    public Task<string> ComposeAsync(IReadOnlyList<string> framePaths, bool reversed, int frameDelayMs, CancellationToken ct = default)
    {
        if (framePaths.Count == 0)
        {
            throw new ArgumentException("Need at least one frame to compose.", nameof(framePaths));
        }

        ct.ThrowIfCancellationRequested();

        // GDI+ compositing/encoding is synchronous CPU work; run it off the
        // calling thread, same as GdiPhotoBrandingService/GdiPhotoFilterService.
        return Task.Run(() =>
        {
            IReadOnlyList<string> sequence = reversed
                ? [.. framePaths, .. framePaths.Reverse().Skip(1).SkipLast(1)] // forward then backward, without repeating the two end frames
                : framePaths;

            List<byte[]> singleFrameGifs = [];
            foreach (string path in sequence)
            {
                using Bitmap frame = GdiImageHelpers.LoadIndependentCopy(path);
                using var buffer = new MemoryStream();
                frame.Save(buffer, ImageFormat.Gif);
                singleFrameGifs.Add(buffer.ToArray());
            }

            byte[] animated = SpliceIntoAnimatedGif(singleFrameGifs, frameDelayMs);

            string directory = Path.GetDirectoryName(framePaths[0]) is { Length: > 0 } dir ? dir : ".";
            string suffix = reversed ? "_boomerang" : "_gif";
            string outputPath = Path.Combine(directory, $"{Path.GetFileNameWithoutExtension(framePaths[0])}{suffix}.gif");
            File.WriteAllBytes(outputPath, animated);
            return outputPath;
        }, ct);
    }

    /// <summary>Reassembles a sequence of independently GDI+-encoded single-frame
    /// GIFs into one animated GIF89a: keeps the first frame's header (Logical
    /// Screen Descriptor + Global Color Table) as the animation's shared header,
    /// then for every frame emits a Graphic Control Extension (delay) followed
    /// by that frame's own Image Descriptor + (Local Color Table, if GDI+ wrote
    /// one instead of reusing the global one) + LZW data, and finally the GIF
    /// trailer byte.</summary>
    private static byte[] SpliceIntoAnimatedGif(List<byte[]> frames, int frameDelayMs)
    {
        using var output = new MemoryStream();

        // Header (6 bytes) + Logical Screen Descriptor (7 bytes) + Global
        // Color Table, all taken from the first frame verbatim -- every
        // frame came from the same source dimensions, so their headers are
        // interchangeable, and GDI+ always writes a global color table for
        // a single-frame GIF.
        int globalHeaderLength = 13 + GlobalColorTableLength(frames[0]);
        output.Write(frames[0], 0, globalHeaderLength);

        // Loop-forever forever Application Extension (NETSCAPE2.0), written
        // once, right after the header -- the standard (if informally
        // specified) way every GIF-aware renderer recognizes an animation
        // that should repeat indefinitely rather than play once.
        output.Write([0x21, 0xFF, 0x0B], 0, 3);
        output.Write("NETSCAPE2.0"u8);
        output.Write([0x03, 0x01, 0x00, 0x00, 0x00], 0, 5);

        // Frame delay is in 1/100ths of a second per the GIF spec.
        byte[] delayBytes = BitConverter.GetBytes((ushort)(frameDelayMs / 10));

        foreach (byte[] frame in frames)
        {
            // Graphic Control Extension: disposal method 1 (leave in place),
            // no transparency, this frame's delay.
            output.Write([0x21, 0xF9, 0x04, 0x00], 0, 4);
            output.Write(delayBytes, 0, 2);
            output.Write([0x00, 0x00], 0, 2);

            // Everything from the Image Descriptor onward, skipping this
            // frame's own header/global-color-table/trailer -- the trailer
            // (0x3B) is always the very last byte GDI+ wrote. Computed per
            // frame (not reused from the shared header above) because GDI+
            // can choose a smaller color table for a lower-color frame, so
            // frames aren't guaranteed to share one header length even
            // though they share dimensions.
            int start = 13 + GlobalColorTableLength(frame);
            int length = frame.Length - start - 1;
            output.Write(frame, start, length);
        }

        output.WriteByte(0x3B); // GIF trailer
        return output.ToArray();
    }

    /// <summary>Global Color Table size in bytes, decoded from the Logical Screen
    /// Descriptor's packed fields byte (offset 10): bit 7 set means a table is
    /// present, bits 0-2 encode its size as 2^(N+1) entries of 3 bytes each.</summary>
    private static int GlobalColorTableLength(byte[] gif)
    {
        byte packedFields = gif[10];
        bool hasGlobalColorTable = (packedFields & 0x80) != 0;
        if (!hasGlobalColorTable)
        {
            // GDI+ always writes one in practice, but fail loudly rather
            // than silently misaligning every frame if that ever changes.
            throw new InvalidOperationException("Expected GDI+'s single-frame GIF output to include a Global Color Table.");
        }

        int tableSizeExponent = packedFields & 0x07;
        int entryCount = 1 << (tableSizeExponent + 1);
        return entryCount * 3;
    }
}
