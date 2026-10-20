namespace Orderflow.Contracts;

public record OrderLine(string Sku, int Quantity, decimal UnitPrice);

/// <summary>Published by Orders when a customer submits an order. Starts the order saga.</summary>
public record OrderSubmitted(Guid OrderId, Guid CustomerId, IReadOnlyList<OrderLine> Lines, decimal Total);
