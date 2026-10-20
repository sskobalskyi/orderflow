namespace Orderflow.Contracts;

// Commands issued by the order saga.
public record ReserveStock(Guid OrderId, IReadOnlyList<OrderLine> Lines);
public record ReleaseStock(Guid OrderId);
public record ProcessPayment(Guid OrderId, Guid CustomerId, decimal Amount);

// Replies from Inventory and Payments.
public record StockReserved(Guid OrderId);
public record StockRejected(Guid OrderId, string Reason);
public record PaymentCompleted(Guid OrderId);
public record PaymentFailed(Guid OrderId, string Reason);

// Final outcomes published by the saga.
public record OrderConfirmed(Guid OrderId);
public record OrderRejected(Guid OrderId, string Reason);
