using System.Text;
using SkiaSharp;
using StockPilot.Domain.Common;
using StockPilot.Infrastructure.Storage;

namespace StockPilot.UnitTests.Infrastructure;

public sealed class ImageProcessorTests
{
    private readonly SkiaImageProcessor _processor = new();

    internal static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(new SKColor(90, 60, 240));
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    [Fact]
    public async Task Re_encodes_a_png_as_webp()
    {
        var result = await _processor.ProcessAsync(new MemoryStream(Png(300, 200)), CancellationToken.None);

        result.ContentType.ShouldBe("image/webp");
        (result.Width, result.Height).ShouldBe((300, 200));
        SkiaImageProcessor.HasImageSignature(result.Data).ShouldBeTrue(); // output is a real WebP file
    }

    [Fact]
    public async Task Shrinks_large_images_to_1200px_on_the_long_edge()
    {
        var result = await _processor.ProcessAsync(new MemoryStream(Png(2400, 1200)), CancellationToken.None);

        (result.Width, result.Height).ShouldBe((1200, 600));
    }

    [Fact]
    public async Task Rejects_a_text_file_renamed_to_png()
    {
        var fake = Encoding.UTF8.GetBytes("<script>alert('xss')</script> definitely a png");

        var ex = await Should.ThrowAsync<DomainException>(() => _processor.ProcessAsync(new MemoryStream(fake), CancellationToken.None));

        ex.Code.ShouldBe("INVALID_IMAGE");
    }

    [Fact]
    public async Task Rejects_a_file_with_an_image_signature_but_garbage_inside()
    {
        byte[] corrupt = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3, 4, 5, 6, 7, 8];

        var ex = await Should.ThrowAsync<DomainException>(() => _processor.ProcessAsync(new MemoryStream(corrupt), CancellationToken.None));

        ex.Code.ShouldBe("INVALID_IMAGE");
    }

    [Fact]
    public async Task Rejects_uploads_over_2_mb_without_decoding_them()
    {
        var huge = new byte[SkiaImageProcessor.MaxUploadBytes + 1];
        huge[0] = 0xFF; huge[1] = 0xD8; huge[2] = 0xFF;

        var ex = await Should.ThrowAsync<DomainException>(() => _processor.ProcessAsync(new MemoryStream(huge), CancellationToken.None));

        ex.Code.ShouldBe("FILE_TOO_LARGE");
    }
}
