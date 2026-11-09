using StockPilot.Application.Features.Catalog;

namespace StockPilot.UnitTests.Application;

public sealed class CreateProductValidatorTests
{
    private readonly CreateProductValidator _validator = new();

    private static CreateProductCommand Valid(params VariantInput[] variants) =>
        new("Claw Hammer", null, null, "pcs", variants.Length > 0 ? variants : [new VariantInput("HAM-16", null, null, 200, 345, 5)]);

    [Fact]
    public void Accepts_a_simple_product()
    {
        _validator.Validate(Valid()).IsValid.ShouldBeTrue();
    }

    [Fact]
    public void Requires_at_least_one_variant()
    {
        var result = _validator.Validate(Valid() with { Variants = [] });
        result.Errors.ShouldContain(e => e.ErrorMessage == "Add at least one variant.");
    }

    [Fact]
    public void Rejects_duplicate_skus_ignoring_case_and_spaces()
    {
        var result = _validator.Validate(Valid(new("ham-16", null, null, 1, 1, 0), new(" HAM-16 ", "B", null, 1, 1, 0)));
        result.Errors.ShouldContain(e => e.ErrorMessage == "Each variant needs a different SKU.");
    }

    [Theory]
    [InlineData("HAM 16")]
    [InlineData("HAM#16")]
    [InlineData("")]
    public void Rejects_invalid_skus(string sku)
    {
        _validator.Validate(Valid(new VariantInput(sku, null, null, 1, 1, 0))).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_negative_prices()
    {
        _validator.Validate(Valid(new VariantInput("A", null, null, -1, 1, 0))).IsValid.ShouldBeFalse();
    }

    [Fact]
    public void Rejects_unknown_units()
    {
        _validator.Validate(Valid() with { UnitOfMeasure = "parsecs" }).IsValid.ShouldBeFalse();
    }
}
