using StockPilot.Domain.Common;
using StockPilot.Domain.Inventory;

namespace StockPilot.UnitTests.Domain;

public sealed class WarehouseStockTests
{
    private static WarehouseStock NewStock() => new(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());

    [Fact]
    public void Receiving_re_weights_the_average_cost()
    {
        var stock = NewStock();
        stock.Receive(10, 100m);
        stock.Receive(30, 120m);

        stock.QuantityOnHand.ShouldBe(40);
        stock.AverageUnitCost.ShouldBe(115m); // (10×100 + 30×120) / 40
    }

    [Fact]
    public void Issuing_does_not_change_the_average_cost()
    {
        var stock = NewStock();
        stock.Receive(40, 115m);
        stock.Issue(15, allowNegative: false);

        stock.QuantityOnHand.ShouldBe(25);
        stock.AverageUnitCost.ShouldBe(115m);
    }

    [Fact]
    public void Cannot_issue_more_than_available()
    {
        var stock = NewStock();
        stock.Receive(3, 10m);

        var ex = Should.Throw<InsufficientStockException>(() => stock.Issue(5, allowNegative: false));

        ex.Code.ShouldBe("INSUFFICIENT_STOCK");
        ex.Available.ShouldBe(3);
        stock.QuantityOnHand.ShouldBe(3); // unchanged
    }

    [Fact]
    public void Company_policy_can_allow_negative_stock()
    {
        var stock = NewStock();
        stock.Receive(3, 10m);
        stock.Issue(5, allowNegative: true);
        stock.QuantityOnHand.ShouldBe(-2);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Quantities_must_be_positive(decimal quantity)
    {
        Should.Throw<DomainException>(() => NewStock().Receive(quantity, 1m)).Code.ShouldBe("QUANTITY_INVALID");
    }

    [Fact]
    public void Adjusting_to_a_count_returns_the_signed_difference()
    {
        var stock = NewStock();
        stock.Receive(10, 5m);

        stock.AdjustTo(7).ShouldBe(-3);
        stock.QuantityOnHand.ShouldBe(7);
        stock.AdjustTo(12).ShouldBe(5);
    }

    [Fact]
    public void Receiving_into_negative_stock_resets_the_cost_basis()
    {
        var stock = NewStock();
        stock.Issue(2, allowNegative: true);
        stock.Receive(10, 50m);

        stock.QuantityOnHand.ShouldBe(8);
        stock.AverageUnitCost.ShouldBe(50m);
    }

    [Fact]
    public void Missing_cost_is_initialised_but_an_existing_average_is_kept()
    {
        var stock = NewStock();
        stock.InitializeCostIfMissing(40m);
        stock.AverageUnitCost.ShouldBe(40m);

        stock.InitializeCostIfMissing(99m);
        stock.AverageUnitCost.ShouldBe(40m);
    }
}

public sealed class StockTransferTests
{
    [Fact]
    public void Source_and_destination_must_differ()
    {
        var warehouse = Guid.CreateVersion7();
        Should.Throw<DomainException>(() => new StockTransfer(Guid.CreateVersion7(), "TR-000001", warehouse, warehouse, null, null, DateTimeOffset.UtcNow))
            .Code.ShouldBe("TRANSFER_SAME_WAREHOUSE");
    }

    [Fact]
    public void A_variant_appears_once_per_transfer()
    {
        var transfer = new StockTransfer(Guid.CreateVersion7(), "TR-000001", Guid.CreateVersion7(), Guid.CreateVersion7(), null, null, DateTimeOffset.UtcNow);
        var variant = Guid.CreateVersion7();
        transfer.AddItem(variant, 2, 10m);

        Should.Throw<DomainException>(() => transfer.AddItem(variant, 1, 10m)).Code.ShouldBe("DUPLICATE_LINE");
    }
}
