using Orderflow.Contracts;

namespace Orderflow.Orders.Domain;

public enum OrderStatus { Submitted, Confirmed, Rejected, Shipped, Returned, Refunded }

public class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order() { }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public string Currency { get; private set; } = "USD";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public DateTimeOffset? ShippedAt { get; private set; }
    public DateTimeOffset? ReturnedAt { get; private set; }
    public DateTimeOffset? ReturnReceivedAt { get; private set; }
    public DateTimeOffset? RefundedAt { get; private set; }
    /// <summary>When an outside system reported the goods put into use, for example a controller linked to its owner.</summary>
    public DateTimeOffset? ActivatedAt { get; private set; }
    public decimal? RefundAmount { get; private set; }
    public string? RefundReason { get; private set; }
    public string? RejectionReason { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;
    public decimal Total => _lines.Sum(l => l.Quantity * l.UnitPrice);

    public static Order Submit(Guid customerId, IEnumerable<OrderLine> lines, string currency, TimeProvider clock)
    {
        var list = lines.ToList();
        if (list.Count == 0)
            throw new DomainException("order_empty", "An order needs at least one line.");
        if (list.Any(l => l.Quantity <= 0 || l.UnitPrice < 0 || string.IsNullOrWhiteSpace(l.Sku)))
            throw new DomainException("order_invalid_line", "Every line needs a SKU, a positive quantity and a non-negative price.");

        var order = new Order
        {
            Id = Guid.NewGuid(),
            CustomerId = customerId,
            Status = OrderStatus.Submitted,
            Currency = currency,
            CreatedAt = clock.GetUtcNow(),
        };
        order._lines.AddRange(list);
        return order;
    }

    public void Confirm(TimeProvider clock)
    {
        if (Status != OrderStatus.Submitted) return;
        Status = OrderStatus.Confirmed;
        ConfirmedAt = clock.GetUtcNow();
    }

    public void Reject(string reason)
    {
        if (Status != OrderStatus.Submitted) return;
        Status = OrderStatus.Rejected;
        RejectionReason = reason;
    }

    /// <summary>An admin hands a confirmed order to the carrier.</summary>
    public void Ship(TimeProvider clock)
    {
        if (Status != OrderStatus.Confirmed)
            throw new DomainException("order_not_shippable", "Only a confirmed order can be shipped.");
        Status = OrderStatus.Shipped;
        ShippedAt = clock.GetUtcNow();
    }

    /// <summary>
    /// An outside system says the goods of this order are in use. Only a shipped order can be activated, and only once:
    /// a second report, or one that arrives for an order in another state, changes nothing.
    /// </summary>
    public bool MarkActivated(DateTimeOffset at)
    {
        if (Status != OrderStatus.Shipped || ActivatedAt is not null) return false;
        ActivatedAt = at;
        return true;
    }

    /// <summary>The customer sends a shipped order back.</summary>
    public void RegisterReturn(TimeProvider clock)
    {
        if (Status != OrderStatus.Shipped)
            throw new DomainException("order_not_returnable", "Only a shipped order can be returned.");
        Status = OrderStatus.Returned;
        ReturnedAt = clock.GetUtcNow();
    }

    /// <summary>The customer or an admin cancels a return that has not been received yet: the goods stay with the customer.</summary>
    public void CancelReturn()
    {
        if (Status != OrderStatus.Returned || ReturnReceivedAt is not null)
            throw new DomainException("order_return_not_cancellable", "Only a return that was not received yet can be cancelled.");
        Status = OrderStatus.Shipped;
        ReturnedAt = null;
    }

    /// <summary>An admin confirms the returned goods arrived back in the warehouse.</summary>
    public void ReceiveReturn(TimeProvider clock)
    {
        if (Status != OrderStatus.Returned)
            throw new DomainException("order_return_not_receivable", "Only a registered return can be received.");
        if (ReturnReceivedAt is not null)
            throw new DomainException("order_return_already_received", "This return was already received.");
        ReturnReceivedAt = clock.GetUtcNow();
    }

    /// <summary>Checks a refund request before money moves. Returns the amount to refund.</summary>
    public decimal EnsureRefundable(decimal? requested)
    {
        if (Status is OrderStatus.Submitted or OrderStatus.Rejected)
            throw new DomainException("order_not_refundable", "Only a paid order can be refunded.");
        if (Status == OrderStatus.Refunded)
            throw new DomainException("order_already_refunded", "This order was already refunded.");

        var amount = requested ?? Total;
        if (amount <= 0 || amount > Total)
            throw new DomainException("refund_invalid_amount", "The refund must be positive and at most the order total.");
        return amount;
    }

    /// <summary>Applied once Payments confirms the money went back. Idempotent.</summary>
    public bool MarkRefunded(decimal amount, string reason, TimeProvider clock)
    {
        if (Status == OrderStatus.Refunded) return false;
        Status = OrderStatus.Refunded;
        RefundAmount = amount;
        RefundReason = reason;
        RefundedAt = clock.GetUtcNow();
        return true;
    }

    public OrderSubmitted ToSubmittedEvent() => new(Id, CustomerId, Lines, Total);
}

/// <summary>A business rule violation. The code is a stable key clients use to localize the message.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
