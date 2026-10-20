using Orderflow.Contracts;

namespace Orderflow.Orders.Domain;

public enum OrderStatus { Submitted, Confirmed, Rejected }

public class Order
{
    private readonly List<OrderLine> _lines = [];

    private Order() { }

    public Guid Id { get; private set; }
    public Guid CustomerId { get; private set; }
    public OrderStatus Status { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public IReadOnlyList<OrderLine> Lines => _lines;
    public decimal Total => _lines.Sum(l => l.Quantity * l.UnitPrice);

    public static Order Submit(Guid customerId, IEnumerable<OrderLine> lines, TimeProvider clock)
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
            CreatedAt = clock.GetUtcNow(),
        };
        order._lines.AddRange(list);
        return order;
    }

    public string? RejectionReason { get; private set; }

    public void Confirm()
    {
        if (Status == OrderStatus.Submitted) Status = OrderStatus.Confirmed;
    }

    public void Reject(string reason)
    {
        if (Status != OrderStatus.Submitted) return;
        Status = OrderStatus.Rejected;
        RejectionReason = reason;
    }

    public OrderSubmitted ToSubmittedEvent() => new(Id, CustomerId, Lines, Total);
}

/// <summary>A business rule violation. The code is a stable key clients use to localize the message.</summary>
public class DomainException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
