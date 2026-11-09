using Microsoft.EntityFrameworkCore;
using StockPilot.Application.Common.Abstractions;
using StockPilot.Application.Common.Exceptions;
using StockPilot.Domain.Catalog;
using StockPilot.Domain.Common;

namespace StockPilot.Application.Features.Catalog;

public sealed class UploadProductImageHandler(IAppDbContext db, IImageProcessor images, IFileStorage storage)
{
    public async Task<ProductImageDto> Handle(Guid productId, Stream content, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new NotFoundException("Product", productId);

        if (product.Images.Count >= Product.MaxImages)
        {
            throw new DomainException("TOO_MANY_IMAGES", $"A product can have at most {Product.MaxImages} images.");
        }

        var processed = await images.ProcessAsync(content, ct);

        // Server-generated key: user file names never become storage paths (no path traversal, no collisions).
        var key = $"products/{Guid.CreateVersion7():N}.webp";
        await storage.SaveAsync(product.TenantId, key, processed.ContentType, processed.Data, ct);

        var image = product.AddImage(key, processed.ContentType, processed.Data.LongLength, processed.Width, processed.Height);
        await db.SaveChangesAsync(ct);

        return new ProductImageDto(image.Id, image.Width, image.Height, image.SizeBytes, image.IsPrimary, image.SortOrder);
    }
}

public sealed class DeleteProductImageHandler(IAppDbContext db, IFileStorage storage)
{
    public async Task Handle(Guid productId, Guid imageId, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new NotFoundException("Product", productId);

        var removed = product.RemoveImage(imageId);
        await db.SaveChangesAsync(ct);

        // After the metadata commit: if this fails, an unreferenced file remains (harmless), never a broken image.
        await storage.DeleteAsync(removed.StorageKey, ct);
    }
}

public sealed class SetPrimaryProductImageHandler(IAppDbContext db)
{
    public async Task Handle(Guid productId, Guid imageId, CancellationToken ct)
    {
        var product = await db.Products.Include(p => p.Images).SingleOrDefaultAsync(p => p.Id == productId, ct)
            ?? throw new NotFoundException("Product", productId);

        product.SetPrimaryImage(imageId);
        await db.SaveChangesAsync(ct);
    }
}

public sealed class GetProductImageHandler(IAppDbContext db, IFileStorage storage)
{
    public async Task<StoredFileContent> Handle(Guid productId, Guid imageId, CancellationToken ct)
    {
        // Both lookups are tenant-filtered: another company's image ID is simply "not found".
        var key = await db.ProductImages
            .Where(i => i.Id == imageId && i.ProductId == productId)
            .Select(i => i.StorageKey)
            .SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("Image", imageId);

        return await storage.ReadAsync(key, ct) ?? throw new NotFoundException("Image", imageId);
    }
}
