using StockPilot.Domain.Catalog;
using StockPilot.Domain.Common;

namespace StockPilot.UnitTests.Domain;

public sealed class ProductTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();

    private static Product NewProduct()
    {
        var product = new Product(Tenant, "LED Bulb 9W", null, null, "pcs");
        product.AddVariant("led-9w", null, "4800000000001", 60m, 95m, 10);
        return product;
    }

    [Fact]
    public void Sku_is_trimmed_and_upper_cased()
    {
        NewProduct().Variants.Single().Sku.ShouldBe("LED-9W");
    }

    [Fact]
    public void Rejects_an_unsupported_unit()
    {
        Should.Throw<DomainException>(() => new Product(Tenant, "X", null, null, "furlong")).Code.ShouldBe("UNIT_INVALID");
    }

    [Fact]
    public void Rejects_the_same_sku_twice_in_one_product()
    {
        var product = NewProduct();
        Should.Throw<DomainException>(() => product.AddVariant(" LED-9W ", "dup", null, 1, 1, 0)).Code.ShouldBe("SKU_DUPLICATE");
    }

    [Theory]
    [InlineData(-1, 10)]
    [InlineData(10, -0.01)]
    public void Rejects_negative_prices(decimal cost, decimal price)
    {
        Should.Throw<DomainException>(() => NewProduct().AddVariant("NEW", null, null, cost, price, 0)).Code.ShouldBe("PRICE_NEGATIVE");
    }

    [Fact]
    public void Cannot_archive_the_last_active_variant()
    {
        var product = NewProduct();
        Should.Throw<DomainException>(() => product.ArchiveVariant(product.Variants.Single().Id)).Code.ShouldBe("LAST_VARIANT");
    }

    [Fact]
    public void Can_archive_a_variant_while_another_stays_active()
    {
        var product = NewProduct();
        var second = product.AddVariant("LED-9W-WW", "Warm", null, 60m, 95m, 0);

        product.ArchiveVariant(second.Id);

        second.IsActive.ShouldBeFalse();
    }

    [Fact]
    public void Archived_product_rejects_variant_changes_until_restored()
    {
        var product = NewProduct();
        product.Archive();

        Should.Throw<DomainException>(() => product.AddVariant("X", null, null, 1, 1, 0)).Code.ShouldBe("PRODUCT_ARCHIVED");

        product.Restore();
        product.AddVariant("X", null, null, 1, 1, 0).ShouldNotBeNull();
    }

    [Fact]
    public void First_image_becomes_primary_and_removing_it_promotes_the_next()
    {
        var product = NewProduct();
        var first = product.AddImage("k1", "image/webp", 100, 10, 10);
        var second = product.AddImage("k2", "image/webp", 100, 10, 10);

        first.IsPrimary.ShouldBeTrue();
        second.IsPrimary.ShouldBeFalse();

        product.RemoveImage(first.Id);

        second.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Exactly_one_primary_image_after_switching()
    {
        var product = NewProduct();
        product.AddImage("k1", "image/webp", 1, 1, 1);
        var second = product.AddImage("k2", "image/webp", 1, 1, 1);

        product.SetPrimaryImage(second.Id);

        product.Images.Count(i => i.IsPrimary).ShouldBe(1);
        second.IsPrimary.ShouldBeTrue();
    }

    [Fact]
    public void Limits_images_per_product()
    {
        var product = NewProduct();
        for (var i = 0; i < Product.MaxImages; i++)
        {
            product.AddImage($"k{i}", "image/webp", 1, 1, 1);
        }

        Should.Throw<DomainException>(() => product.AddImage("one-too-many", "image/webp", 1, 1, 1)).Code.ShouldBe("TOO_MANY_IMAGES");
    }
}

public sealed class ProductCategoryTests
{
    private static readonly Guid Tenant = Guid.CreateVersion7();

    [Fact]
    public void Allows_one_level_of_nesting_only()
    {
        var electrical = new ProductCategory(Tenant, "Electrical", null, null);
        var wires = new ProductCategory(Tenant, "Wires", null, electrical);

        wires.ParentId.ShouldBe(electrical.Id);
        Should.Throw<DomainException>(() => new ProductCategory(Tenant, "Copper", null, wires)).Code.ShouldBe("CATEGORY_TOO_DEEP");
    }

    [Fact]
    public void Cannot_be_its_own_parent()
    {
        var category = new ProductCategory(Tenant, "Tools", null, null);
        Should.Throw<DomainException>(() => category.Update("Tools", null, category)).Code.ShouldBe("CATEGORY_PARENT_INVALID");
    }

    [Fact]
    public void Cannot_use_another_companys_category_as_parent()
    {
        var foreign = new ProductCategory(Guid.CreateVersion7(), "Theirs", null, null);
        Should.Throw<DomainException>(() => new ProductCategory(Tenant, "Mine", null, foreign)).Code.ShouldBe("CATEGORY_PARENT_INVALID");
    }
}
