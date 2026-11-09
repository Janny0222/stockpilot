using SkiaSharp;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Domain.Common;

namespace StockPilot.Infrastructure.Storage;

/// <summary>
/// Turns an untrusted upload into a safe, small image:
///  1. Size cap (2 MB) enforced while reading, so a huge body is never fully buffered.
///  2. File signature ("magic bytes") must be JPEG, PNG or WebP. The extension and the client's
///     Content-Type are ignored, because both are attacker-controlled.
///  3. Dimension cap before decoding, which stops "decompression bombs" (tiny files that expand to GBs).
///  4. Decode → apply EXIF orientation → resize to ≤ 1200 px → re-encode as WebP.
///     Re-encoding drops all metadata (GPS location, camera serial) and anything smuggled inside the
///     original file (polyglots, scripts). The stored bytes are pixels we produced ourselves.
/// </summary>
internal sealed class SkiaImageProcessor : IImageProcessor
{
    public const int MaxUploadBytes = 2 * 1024 * 1024;
    private const int MaxPixels = 40_000_000;
    private const int MaxEdge = 1200;
    private const int WebpQuality = 82;

    public async Task<ProcessedImage> ProcessAsync(Stream input, CancellationToken cancellationToken)
    {
        var bytes = await ReadCappedAsync(input, cancellationToken);

        if (!HasImageSignature(bytes))
        {
            throw Invalid();
        }

        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data) ?? throw Invalid();

        if ((long)codec.Info.Width * codec.Info.Height > MaxPixels)
        {
            throw new DomainException("IMAGE_TOO_LARGE", "That image's dimensions are too large. Use one under 40 megapixels.");
        }

        using var decoded = SKBitmap.Decode(codec) ?? throw Invalid();
        using var upright = ApplyOrientation(decoded, codec.EncodedOrigin);

        var scale = Math.Min(1d, (double)MaxEdge / Math.Max(upright.Width, upright.Height));
        var width = Math.Max(1, (int)Math.Round(upright.Width * scale));
        var height = Math.Max(1, (int)Math.Round(upright.Height * scale));

        using var resized = scale < 1
            ? upright.Resize(new SKImageInfo(width, height), new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            : upright.Copy();
        using var image = SKImage.FromBitmap(resized);
        using var encoded = image.Encode(SKEncodedImageFormat.Webp, WebpQuality) ?? throw Invalid();

        return new ProcessedImage(encoded.ToArray(), "image/webp", width, height);
    }

    private static async Task<byte[]> ReadCappedAsync(Stream input, CancellationToken ct)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await input.ReadAsync(chunk, ct)) > 0)
        {
            if (buffer.Length + read > MaxUploadBytes)
            {
                throw new DomainException("FILE_TOO_LARGE", "Images must be 2 MB or smaller.");
            }

            buffer.Write(chunk, 0, read);
        }

        return buffer.ToArray();
    }

    internal static bool HasImageSignature(ReadOnlySpan<byte> b) =>
        b.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF]) ||                                  // JPEG
        b.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]) ||    // PNG
        (b.Length >= 12 && b[..4].SequenceEqual("RIFF"u8) && b[8..12].SequenceEqual("WEBP"u8)); // WebP

    /// <summary>Phone photos are often stored sideways with an EXIF "rotate me" flag; bake the rotation in.</summary>
    private static SKBitmap ApplyOrientation(SKBitmap source, SKEncodedOrigin origin)
    {
        if (origin is SKEncodedOrigin.TopLeft or SKEncodedOrigin.Default)
        {
            return source.Copy();
        }

        var swap = origin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
        var result = new SKBitmap(swap ? source.Height : source.Width, swap ? source.Width : source.Height);
        using var canvas = new SKCanvas(result);

        switch (origin)
        {
            case SKEncodedOrigin.BottomRight:
                canvas.RotateDegrees(180, source.Width / 2f, source.Height / 2f);
                break;
            case SKEncodedOrigin.RightTop:
                canvas.Translate(result.Width, 0);
                canvas.RotateDegrees(90);
                break;
            case SKEncodedOrigin.LeftBottom:
                canvas.Translate(0, result.Height);
                canvas.RotateDegrees(270);
                break;
            default:
                break; // mirrored variants are rare; leave as-is rather than guess
        }

        using var sourceImage = SKImage.FromBitmap(source);
        canvas.DrawImage(sourceImage, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
        return result;
    }

    private static DomainException Invalid() =>
        new("INVALID_IMAGE", "Upload a JPEG, PNG or WebP image.");
}
