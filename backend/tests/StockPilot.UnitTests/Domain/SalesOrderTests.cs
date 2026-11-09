using StockPilot.Domain.Common;
using StockPilot.Domain.Inventory;
using StockPilot.Domain.Sales;

namespace StockPilot.UnitTests.Domain;

public sealed class SalesOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static SalesOrder NewOrder(params decimal[] quantities)
    {
        var order = new SalesOrder(Guid.CreateVersion7(), "SO-000001", Guid.CreateVersion7(), Guid.CreateVersion7(), Today, null);
        order.ReplaceItems(quantities.Select(q => (Guid.CreateVersion7(), q, 25m)));
        return order;
    }

    private static SalesOrder Confirmed(params decimal[] quantities)
    {
        var order = NewOrder(quantities);
        order.Confirm(null, Now);
        return order;
    }

    [Fact]
    public void Total_is_quantity_times_price()
    {
        NewOrder(2, 4).Total.ShouldBe(150m); // (2 + 4) × ₱25
    }

    [Fact]
    public void Confirming_returns_every_line_to_reserve()
    {
        var order = NewOrder(3, 7);
        var toReserve = order.Confirm(null, Now);

        order.Status.ShouldBe(SalesOrderStatus.Confirmed);
        toReserve.Select(r => r.Quantity).ShouldBe([3m, 7m], ignoreOrder: true);
    }

    [Fact]
    public void An_empty_order_cannot_be_confirmed_and_a_confirmed_one_cannot_be_edited()
    {
        var empty = new SalesOrder(Guid.CreateVersion7(), "SO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), Today, null);
        Should.Throw<DomainException>(() => empty.Confirm(null, Now)).Code.ShouldBe("ORDER_EMPTY");

        var order = Confirmed(1);
        Should.Throw<DomainException>(() => order.ReplaceItems([(Guid.CreateVersion7(), 1m, 1m)])).Code.ShouldBe("INVALID_STATUS");
        Should.Throw<DomainException>(() => order.Confirm(null, Now)).Code.ShouldBe("INVALID_STATUS");
    }

    [Fact]
    public void A_draft_cannot_be_shipped()
    {
        var order = NewOrder(5);
        Should.Throw<DomainException>(() => order.Ship(new Dictionary<Guid, decimal> { [order.Items.Single().Id] = 1 }, Now))
            .Code.ShouldBe("INVALID_STATUS");
    }

    [Fact]
    public void Partial_then_full_shipment_moves_the_status()
    {
        var order = Confirmed(10);
        var item = order.Items.Single();

        order.Ship(new Dictionary<Guid, decimal> { [item.Id] = 4 }, Now);
        order.Status.ShouldBe(SalesOrderStatus.PartiallyFulfilled);
        item.QuantityRemaining.ShouldBe(6);

        order.Ship(new Dictionary<Guid, decimal> { [item.Id] = 6 }, Now);
        order.Status.ShouldBe(SalesOrderStatus.Fulfilled);
    }

    [Fact]
    public void Cannot_ship_more_than_remains()
    {
        var order = Confirmed(3);
        Should.Throw<DomainException>(() => order.Ship(new Dictionary<Guid, decimal> { [order.Items.Single().Id] = 4 }, Now))
            .Code.ShouldBe("OVER_SHIPMENT");
    }

    [Fact]
    public void Cancelling_a_draft_releases_nothing()
    {
        NewOrder(5).Cancel("Wrong customer", Now).ShouldBeEmpty();
    }

    [Fact]
    public void Cancelling_a_partly_shipped_order_releases_only_the_unshipped_remainder()
    {
        var order = Confirmed(10);
        order.Ship(new Dictionary<Guid, decimal> { [order.Items.Single().Id] = 4 }, Now);

        var release = order.Cancel("Customer reduced the order", Now);

        order.Status.ShouldBe(SalesOrderStatus.Cancelled);
        release.Single().Quantity.ShouldBe(6);
    }

    [Fact]
    public void A_fulfilled_order_cannot_be_cancelled_and_a_reason_is_required()
    {
        var order = Confirmed(2);
        Should.Throw<DomainException>(() => order.Cancel(" ", Now)).Code.ShouldBe("REASON_REQUIRED");

        order.Ship(new Dictionary<Guid, decimal> { [order.Items.Single().Id] = 2 }, Now);
        Should.Throw<DomainException>(() => order.Cancel("Too late", Now)).Code.ShouldBe("INVALID_STATUS");
    }
}

public sealed class StockReservationTests
{
    private static WarehouseStock StockOf(decimal onHand)
    {
        var stock = new WarehouseStock(Guid.CreateVersion7(), Guid.CreateVersion7(), Guid.CreateVersion7());
        stock.Receive(onHand, 50m);
        return stock;
    }

    [Fact]
    public void Reserving_lowers_available_but_not_on_hand()
    {
        var stock = StockOf(10);
        stock.Reserve(4, allowNegative: false);

        stock.QuantityOnHand.ShouldBe(10);
        stock.QuantityReserved.ShouldBe(4);
        stock.QuantityAvailable.ShouldBe(6);
    }

    [Fact]
    public void Cannot_reserve_more_than_available_and_the_error_names_the_sku()
    {
        var stock = StockOf(10);
        stock.Reserve(8, allowNegative: false);

        var error = Should.Throw<InsufficientStockException>(() => stock.Reserve(3, allowNegative: false, sku: "LED-9W"));
        error.Available.ShouldBe(2);
        error.Message.ShouldStartWith("LED-9W:");
    }

    [Fact]
    public void Reserved_stock_cannot_be_issued_by_a_manual_stock_out()
    {
        var stock = StockOf(10);
        stock.Reserve(8, allowNegative: false);

        Should.Throw<InsufficientStockException>(() => stock.Issue(3, allowNegative: false));
    }

    [Fact]
    public void Shipping_takes_units_out_of_both_on_hand_and_reserved()
    {
        var stock = StockOf(10);
        stock.Reserve(6, allowNegative: false);
        stock.ShipReserved(4);

        stock.QuantityOnHand.ShouldBe(6);
        stock.QuantityReserved.ShouldBe(2);
        stock.QuantityAvailable.ShouldBe(4);
        Should.Throw<DomainException>(() => stock.ShipReserved(3)).Code.ShouldBe("NOT_RESERVED");
    }

    [Fact]
    public void Releasing_gives_units_back_and_never_goes_below_zero()
    {
        var stock = StockOf(10);
        stock.Reserve(5, allowNegative: false);
        stock.Release(7);

        stock.QuantityReserved.ShouldBe(0);
        stock.QuantityAvailable.ShouldBe(10);
    }
}
