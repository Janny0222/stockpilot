using StockPilot.Domain.Common;
using StockPilot.Domain.Purchasing;

namespace StockPilot.UnitTests.Domain;

public sealed class PurchaseOrderTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 9, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly Today = new(2026, 11, 9);

    private static PurchaseOrder NewOrder(params decimal[] quantities)
    {
        var po = new PurchaseOrder(Guid.CreateVersion7(), "PO-000001", Guid.CreateVersion7(), Guid.CreateVersion7(), Today, Today.AddDays(7), null);
        po.ReplaceItems(quantities.Select(q => (Guid.CreateVersion7(), q, 10m)));
        return po;
    }

    private static PurchaseOrder Approved(params decimal[] quantities)
    {
        var po = NewOrder(quantities);
        po.Submit(null, Now);
        po.Approve(null, Now);
        return po;
    }

    [Fact]
    public void Total_is_the_sum_of_line_totals()
    {
        NewOrder(3, 2).Total.ShouldBe(50m); // (3 + 2) × ₱10
    }

    [Fact]
    public void An_empty_order_cannot_be_submitted()
    {
        var po = new PurchaseOrder(Guid.CreateVersion7(), "PO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), Today, null, null);
        Should.Throw<DomainException>(() => po.Submit(null, Now)).Code.ShouldBe("ORDER_EMPTY");
    }

    [Fact]
    public void Lines_are_locked_once_submitted()
    {
        var po = NewOrder(5);
        po.Submit(null, Now);
        Should.Throw<DomainException>(() => po.ReplaceItems([(Guid.CreateVersion7(), 1m, 1m)])).Code.ShouldBe("INVALID_STATUS");
    }

    [Fact]
    public void Only_submitted_orders_can_be_approved()
    {
        Should.Throw<DomainException>(() => NewOrder(5).Approve(null, Now)).Code.ShouldBe("INVALID_STATUS");
    }

    [Fact]
    public void Cannot_receive_before_approval()
    {
        var po = NewOrder(5);
        po.Submit(null, Now);
        Should.Throw<DomainException>(() => po.Receive(new Dictionary<Guid, decimal> { [po.Items.Single().Id] = 1 }, Now)).Code.ShouldBe("INVALID_STATUS");
    }

    [Fact]
    public void Partial_then_full_receipt_moves_the_status_forward()
    {
        var po = Approved(10, 4);
        var (a, b) = (po.Items.First(), po.Items.Last());

        po.Receive(new Dictionary<Guid, decimal> { [a.Id] = 6 }, Now);
        po.Status.ShouldBe(PurchaseOrderStatus.PartiallyReceived);
        a.QuantityRemaining.ShouldBe(4);

        po.Receive(new Dictionary<Guid, decimal> { [a.Id] = 4, [b.Id] = 4 }, Now);
        po.Status.ShouldBe(PurchaseOrderStatus.Received);
        po.LastReceivedAt.ShouldBe(Now);
    }

    [Fact]
    public void Cannot_receive_more_than_was_ordered()
    {
        var po = Approved(10);
        Should.Throw<DomainException>(() => po.Receive(new Dictionary<Guid, decimal> { [po.Items.Single().Id] = 11 }, Now)).Code.ShouldBe("OVER_RECEIPT");
    }

    [Fact]
    public void Cannot_cancel_once_goods_arrived()
    {
        var po = Approved(10);
        po.Receive(new Dictionary<Guid, decimal> { [po.Items.Single().Id] = 1 }, Now);
        Should.Throw<DomainException>(() => po.Cancel("changed mind", Now)).Code.ShouldBe("INVALID_STATUS");
    }

    [Fact]
    public void Cancelling_needs_a_reason()
    {
        Should.Throw<DomainException>(() => NewOrder(1).Cancel(" ", Now)).Code.ShouldBe("REASON_REQUIRED");
    }

    [Fact]
    public void Expected_date_cannot_precede_order_date()
    {
        Should.Throw<DomainException>(() => new PurchaseOrder(Guid.CreateVersion7(), "PO-1", Guid.CreateVersion7(), Guid.CreateVersion7(), Today, Today.AddDays(-1), null))
            .Code.ShouldBe("EXPECTED_BEFORE_ORDER");
    }
}
