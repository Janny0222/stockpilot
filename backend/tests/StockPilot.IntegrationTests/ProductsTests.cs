using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using SkiaSharp;
using StockPilot.Domain.Identity;
using StockPilot.IntegrationTests.Infrastructure;

namespace StockPilot.IntegrationTests;

[Collection(ApiCollection.Name)]
public sealed class ProductsTests(ApiFactory factory)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private static object NewProduct(string name, params (string Sku, string? Barcode, decimal Price)[] variants) => new
    {
        name,
        unitOfMeasure = "pcs",
        variants = (variants.Length > 0 ? variants : [($"SKU-{Guid.NewGuid():N}"[..14], null, 100m)])
            .Select(v => new { sku = v.Sku, barcode = v.Barcode, costPrice = 60m, sellingPrice = v.Price, reorderLevel = 5m }),
    };

    private static async Task<ProductDetail> CreateAsync(HttpClient client, object body)
    {
        var response = await client.PostAsJsonAsync("/api/v1/products", body);
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<ProductDetail>(Json))!;
    }

    [Fact]
    public async Task Owner_creates_a_product_with_variants_and_sees_costs()
    {
        var owner = await factory.RegisterCompanyAsync();

        var created = await CreateAsync(owner.Client, NewProduct("THHN Wire 3.5mm", ("thhn-red", "4800000000011", 2950m), ("THHN-BLK", null, 2950m)));

        created.Variants.Select(v => v.Sku).ShouldBe(["THHN-RED", "THHN-BLK"], ignoreOrder: true);
        created.Variants.ShouldAllBe(v => v.CostPrice == 60m);
        created.CanViewCost.ShouldBeTrue();

        var fetched = await owner.Client.GetFromJsonAsync<ProductDetail>($"/api/v1/products/{created.Id}", Json);
        fetched!.Name.ShouldBe("THHN Wire 3.5mm");
    }

    [Fact]
    public async Task Search_matches_name_sku_and_barcode_and_only_within_the_company()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();
        await CreateAsync(acme.Client, NewProduct("Claw Hammer 16oz", ("HAM-16", "4801111111111", 345m)));
        await CreateAsync(acme.Client, NewProduct("Paint Brush", ("BRS-2", null, 55m)));
        await CreateAsync(globex.Client, NewProduct("Claw Hammer Pro", ("HAM-PRO", null, 500m)));

        async Task<List<string>> Search(string q) =>
            (await acme.Client.GetFromJsonAsync<Page<ListItem>>($"/api/v1/products?search={q}", Json))!.Items.Select(i => i.Name).ToList();

        (await Search("hammer")).ShouldBe(["Claw Hammer 16oz"]); // Globex's hammer is invisible
        (await Search("ham-16")).ShouldBe(["Claw Hammer 16oz"]);
        (await Search("4801111111111")).ShouldBe(["Claw Hammer 16oz"]);
        (await Search("nothing-matches")).ShouldBeEmpty();
    }

    [Fact]
    public async Task Sku_must_be_unique_within_a_company_but_not_across_companies()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();
        await CreateAsync(acme.Client, NewProduct("First", ("DUP-SKU", null, 1m)));

        var duplicate = await acme.Client.PostAsJsonAsync("/api/v1/products", NewProduct("Second", ("dup-sku", null, 1m)));
        duplicate.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await duplicate.ReadProblemAsync()).Code.ShouldBe("SKU_TAKEN");

        await CreateAsync(globex.Client, NewProduct("Theirs", ("DUP-SKU", null, 1m))); // fine: different company
    }

    [Fact]
    public async Task Invalid_product_returns_field_errors()
    {
        var owner = await factory.RegisterCompanyAsync();

        var response = await owner.Client.PostAsJsonAsync("/api/v1/products", new { name = "", unitOfMeasure = "pcs", variants = Array.Empty<object>() });

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await response.ReadProblemAsync()).Errors!.Keys.ShouldBe(["name", "variants"], ignoreOrder: true);
    }

    [Fact]
    public async Task Another_companys_products_and_categories_are_unreachable()
    {
        var acme = await factory.RegisterCompanyAsync();
        var globex = await factory.RegisterCompanyAsync();
        var theirs = await CreateAsync(globex.Client, NewProduct("Secret Product"));
        var theirCategory = await (await globex.Client.PostAsJsonAsync("/api/v1/categories", new { name = "Secret" }))
            .Content.ReadFromJsonAsync<Category>(Json);

        (await acme.Client.GetAsync($"/api/v1/products/{theirs.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.Client.PostAsync($"/api/v1/products/{theirs.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.NotFound);
        (await acme.Client.PutAsJsonAsync($"/api/v1/products/{theirs.Id}", new { name = "Hijacked", unitOfMeasure = "pcs", version = theirs.Version }))
            .StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // Attaching their category to my product by ID must fail like a non-existent category.
        var withTheirCategory = await acme.Client.PostAsJsonAsync("/api/v1/products", new
        {
            name = "Mine", unitOfMeasure = "pcs", categoryId = theirCategory!.Id,
            variants = new[] { new { sku = "MINE-1", costPrice = 1m, sellingPrice = 2m, reorderLevel = 0m } },
        });
        withTheirCategory.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await withTheirCategory.ReadProblemAsync()).Code.ShouldBe("CATEGORY_NOT_FOUND");
    }

    [Fact]
    public async Task Sales_staff_can_view_products_without_costs_but_cannot_change_them()
    {
        var owner = await factory.RegisterCompanyAsync();
        var product = await CreateAsync(owner.Client, NewProduct("Breaker 20A"));
        var sales = await factory.CreateUserAsync(owner.TenantId, SystemRoles.SalesStaffId);

        var seen = await sales.Client.GetFromJsonAsync<ProductDetail>($"/api/v1/products/{product.Id}", Json);
        seen!.CanViewCost.ShouldBeFalse();
        seen.Variants.ShouldAllBe(v => v.CostPrice == null);

        (await sales.Client.PostAsJsonAsync("/api/v1/products", NewProduct("Nope"))).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        (await sales.Client.PostAsync($"/api/v1/products/{product.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Stale_edit_is_rejected_instead_of_overwriting_someone_elses_change()
    {
        var owner = await factory.RegisterCompanyAsync();
        var product = await CreateAsync(owner.Client, NewProduct("Original"));

        var first = await owner.Client.PutAsJsonAsync($"/api/v1/products/{product.Id}", new { name = "Edited by A", unitOfMeasure = "pcs", version = product.Version });
        first.StatusCode.ShouldBe(HttpStatusCode.NoContent);

        // B still holds the version from before A's save.
        var second = await owner.Client.PutAsJsonAsync($"/api/v1/products/{product.Id}", new { name = "Edited by B", unitOfMeasure = "pcs", version = product.Version });
        second.StatusCode.ShouldBe(HttpStatusCode.Conflict);
        (await second.ReadProblemAsync()).Code.ShouldBe("CONCURRENCY_CONFLICT");

        (await owner.Client.GetFromJsonAsync<ProductDetail>($"/api/v1/products/{product.Id}", Json))!.Name.ShouldBe("Edited by A");
    }

    [Fact]
    public async Task Archiving_hides_a_product_from_the_default_list_and_restore_brings_it_back()
    {
        var owner = await factory.RegisterCompanyAsync();
        var product = await CreateAsync(owner.Client, NewProduct("Seasonal Item"));

        (await owner.Client.PostAsync($"/api/v1/products/{product.Id}/archive", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);

        (await owner.Client.GetFromJsonAsync<Page<ListItem>>("/api/v1/products", Json))!.Items.ShouldBeEmpty();
        (await owner.Client.GetFromJsonAsync<Page<ListItem>>("/api/v1/products?status=Archived", Json))!.Items.Single().Id.ShouldBe(product.Id);

        (await owner.Client.PostAsync($"/api/v1/products/{product.Id}/restore", null)).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.Client.GetFromJsonAsync<Page<ListItem>>("/api/v1/products", Json))!.TotalCount.ShouldBe(1);
    }

    [Fact]
    public async Task The_last_active_variant_cannot_be_archived()
    {
        var owner = await factory.RegisterCompanyAsync();
        var product = await CreateAsync(owner.Client, NewProduct("Single"));

        var response = await owner.Client.PostAsync($"/api/v1/products/{product.Id}/variants/{product.Variants[0].Id}/archive", null);

        response.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await response.ReadProblemAsync()).Code.ShouldBe("LAST_VARIANT");
    }

    [Fact]
    public async Task Category_filter_includes_subcategories_and_non_empty_categories_cannot_be_deleted()
    {
        var owner = await factory.RegisterCompanyAsync();
        var electrical = await (await owner.Client.PostAsJsonAsync("/api/v1/categories", new { name = "Electrical" })).Content.ReadFromJsonAsync<Category>(Json);
        var wires = await (await owner.Client.PostAsJsonAsync("/api/v1/categories", new { name = "Wires", parentId = electrical!.Id })).Content.ReadFromJsonAsync<Category>(Json);
        await CreateAsync(owner.Client, new
        {
            name = "THHN", unitOfMeasure = "roll", categoryId = wires!.Id,
            variants = new[] { new { sku = "THHN-X", costPrice = 1m, sellingPrice = 2m, reorderLevel = 0m } },
        });

        var underElectrical = await owner.Client.GetFromJsonAsync<Page<ListItem>>($"/api/v1/products?categoryId={electrical.Id}", Json);
        underElectrical!.Items.Single().Name.ShouldBe("THHN");

        var delete = await owner.Client.DeleteAsync($"/api/v1/categories/{wires.Id}");
        delete.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await delete.ReadProblemAsync()).Code.ShouldBe("CATEGORY_IN_USE");
    }

    [Fact]
    public async Task Images_are_validated_reencoded_and_private_to_the_company()
    {
        var owner = await factory.RegisterCompanyAsync();
        var other = await factory.RegisterCompanyAsync();
        var product = await CreateAsync(owner.Client, NewProduct("LED Floodlight"));

        var upload = await owner.Client.PostAsync($"/api/v1/products/{product.Id}/images", ImageForm(Png(1600, 800), "flood.png"));
        upload.StatusCode.ShouldBe(HttpStatusCode.Created, await upload.Content.ReadAsStringAsync());
        var image = await upload.Content.ReadFromJsonAsync<Image>(Json);
        image!.IsPrimary.ShouldBeTrue();
        (image.Width, image.Height).ShouldBe((1200, 600));

        var file = await owner.Client.GetAsync($"/api/v1/products/{product.Id}/images/{image.Id}");
        file.StatusCode.ShouldBe(HttpStatusCode.OK);
        file.Content.Headers.ContentType!.MediaType.ShouldBe("image/webp");
        file.Headers.CacheControl!.Private.ShouldBeTrue();

        // Another company can't read it, even with both IDs.
        (await other.Client.GetAsync($"/api/v1/products/{product.Id}/images/{image.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);

        // A script renamed to .png is rejected by content, not by name.
        var fake = await owner.Client.PostAsync($"/api/v1/products/{product.Id}/images",
            ImageForm("<svg onload=alert(1)>"u8.ToArray(), "evil.png"));
        fake.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        (await fake.ReadProblemAsync()).Code.ShouldBe("INVALID_IMAGE");

        // Deleting removes it.
        (await owner.Client.DeleteAsync($"/api/v1/products/{product.Id}/images/{image.Id}")).StatusCode.ShouldBe(HttpStatusCode.NoContent);
        (await owner.Client.GetAsync($"/api/v1/products/{product.Id}/images/{image.Id}")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Lookup_finds_a_variant_by_exact_barcode_or_sku()
    {
        var owner = await factory.RegisterCompanyAsync();
        await CreateAsync(owner.Client, NewProduct("Wall Switch", ("SW-1G", "4802222222222", 65m)));

        (await owner.Client.GetFromJsonAsync<Lookup>("/api/v1/products/lookup?code=4802222222222", Json))!.Sku.ShouldBe("SW-1G");
        (await owner.Client.GetFromJsonAsync<Lookup>("/api/v1/products/lookup?code=sw-1g", Json))!.ProductName.ShouldBe("Wall Switch");
        (await owner.Client.GetAsync("/api/v1/products/lookup?code=SW-1")).StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    private static MultipartFormDataContent ImageForm(byte[] bytes, string fileName)
    {
        var content = new ByteArrayContent(bytes);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        return new MultipartFormDataContent { { content, "file", fileName } };
    }

    private static byte[] Png(int width, int height)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(SKColors.Orange);
        }

        using var image = SKImage.FromBitmap(bitmap);
        return image.Encode(SKEncodedImageFormat.Png, 100).ToArray();
    }

    private sealed record Page<T>(List<T> Items, int TotalCount);

    private sealed record ListItem(Guid Id, string Name);

    private sealed record Category(Guid Id, string Name);

    private sealed record Variant(Guid Id, string Sku, decimal? CostPrice);

    private sealed record Image(Guid Id, int Width, int Height, bool IsPrimary);

    private sealed record Lookup(string Sku, string ProductName);

    private sealed record ProductDetail(Guid Id, string Name, uint Version, bool CanViewCost, List<Variant> Variants);
}
