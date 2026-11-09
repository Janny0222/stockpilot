using Microsoft.AspNetCore.Mvc;
using StockPilot.API.Authorization;
using StockPilot.Application.Common.Models;
using StockPilot.Application.Features.Catalog;
using StockPilot.Domain.Identity;

namespace StockPilot.API.Controllers;

public sealed record CreatedIdResponse(Guid Id);

[ApiController]
[Route("api/v1/categories")]
[Produces("application/json")]
public sealed class CategoriesController : ControllerBase
{
    [HttpGet]
    [HasPermission(Permissions.Products.View)]
    public Task<IReadOnlyList<CategoryDto>> List([FromServices] ListCategoriesHandler handler, CancellationToken ct) =>
        handler.Handle(ct);

    [HttpPost]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType<CategoryDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(SaveCategoryCommand command, [FromServices] CreateCategoryHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, await handler.Handle(command, ct));

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    public Task<CategoryDto> Update(Guid id, SaveCategoryCommand command, [FromServices] UpdateCategoryHandler handler, CancellationToken ct) =>
        handler.Handle(id, command, ct);

    [HttpDelete("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid id, [FromServices] DeleteCategoryHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, ct);
        return NoContent();
    }
}

[ApiController]
[Route("api/v1/products")]
[Produces("application/json")]
public sealed class ProductsController : ControllerBase
{
    // ---------------- Read (products.view) ----------------

    [HttpGet]
    [HasPermission(Permissions.Products.View)]
    public Task<PagedResult<ProductListItemDto>> List(
        [FromQuery] ListProductsQuery query, [FromServices] ListProductsHandler handler, CancellationToken ct) =>
        handler.Handle(query, ct);

    [HttpGet("{id:guid}")]
    [HasPermission(Permissions.Products.View)]
    [ProducesResponseType<ProductDetailDto>(StatusCodes.Status200OK)]
    public Task<ProductDetailDto> Get(Guid id, [FromServices] GetProductHandler handler, CancellationToken ct) =>
        handler.Handle(id, ct);

    /// <summary>Exact SKU or barcode match (scanner input).</summary>
    [HttpGet("lookup")]
    [HasPermission(Permissions.Products.View)]
    public Task<VariantLookupDto> Lookup([FromQuery] string code, [FromServices] LookupVariantHandler handler, CancellationToken ct) =>
        handler.Handle(code, ct);

    /// <summary>
    /// Image bytes. Image IDs never change content, so browsers may cache them for a year,
    /// but "private" keeps shared caches (CDNs, proxies) from storing a company's photos.
    /// </summary>
    [HttpGet("{id:guid}/images/{imageId:guid}")]
    [HasPermission(Permissions.Products.View)]
    [Produces("image/webp")]
    public async Task<IActionResult> GetImage(Guid id, Guid imageId, [FromServices] GetProductImageHandler handler, CancellationToken ct)
    {
        var file = await handler.Handle(id, imageId, ct);
        Response.Headers.CacheControl = "private, max-age=31536000, immutable";
        Response.Headers.XContentTypeOptions = "nosniff";
        return File(file.Data, file.ContentType);
    }

    // ---------------- Write (products.manage) ----------------

    [HttpPost]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType<ProductDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create(CreateProductCommand command, [FromServices] CreateProductHandler handler, CancellationToken ct)
    {
        var product = await handler.Handle(command, ct);
        return CreatedAtAction(nameof(Get), new { id = product.Id }, product);
    }

    [HttpPut("{id:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Update(Guid id, UpdateProductCommand command, [FromServices] UpdateProductHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, command, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/archive")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Archive(Guid id, [FromServices] SetProductStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/restore")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Restore(Guid id, [FromServices] SetProductStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, archive: false, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/variants")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType<CreatedIdResponse>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddVariant(Guid id, VariantInput input, [FromServices] AddVariantHandler handler, CancellationToken ct) =>
        StatusCode(StatusCodes.Status201Created, new CreatedIdResponse(await handler.Handle(id, input, ct)));

    [HttpPut("{id:guid}/variants/{variantId:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> UpdateVariant(
        Guid id, Guid variantId, UpdateVariantCommand command, [FromServices] UpdateVariantHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, variantId, command, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/variants/{variantId:guid}/archive")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> ArchiveVariant(Guid id, Guid variantId, [FromServices] SetVariantStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, variantId, archive: true, ct);
        return NoContent();
    }

    [HttpPost("{id:guid}/variants/{variantId:guid}/restore")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RestoreVariant(Guid id, Guid variantId, [FromServices] SetVariantStatusHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, variantId, archive: false, ct);
        return NoContent();
    }

    /// <summary>multipart/form-data with one "file" field. 3 MB request cap; the image itself must be ≤ 2 MB.</summary>
    [HttpPost("{id:guid}/images")]
    [HasPermission(Permissions.Products.Manage)]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(3 * 1024 * 1024)]
    [ProducesResponseType<ProductImageDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> UploadImage(Guid id, IFormFile file, [FromServices] UploadProductImageHandler handler, CancellationToken ct)
    {
        await using var stream = file.OpenReadStream();
        return StatusCode(StatusCodes.Status201Created, await handler.Handle(id, stream, ct));
    }

    [HttpPost("{id:guid}/images/{imageId:guid}/primary")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> SetPrimaryImage(Guid id, Guid imageId, [FromServices] SetPrimaryProductImageHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, imageId, ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}/images/{imageId:guid}")]
    [HasPermission(Permissions.Products.Manage)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> DeleteImage(Guid id, Guid imageId, [FromServices] DeleteProductImageHandler handler, CancellationToken ct)
    {
        await handler.Handle(id, imageId, ct);
        return NoContent();
    }
}
