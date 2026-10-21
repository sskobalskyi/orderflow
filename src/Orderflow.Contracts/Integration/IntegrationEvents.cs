namespace Orderflow.Contracts.Integration;

/// <summary>
/// Versioned business events other systems consume. They are published as raw JSON to the topic exchange
/// `integration.events` with the address as routing key, and match `contracts/integration-events.yaml`.
/// Deliberately separate from the internal saga messages in <see cref="Messages"/>, which carry only ids
/// and may change freely.
/// </summary>
public static class IntegrationAddress
{
    public const string Exchange = "integration.events";
    public const string OrderConfirmed = "order.confirmed.v1";
    public const string OrderShipped = "order.shipped.v1";
    public const string OrderReturned = "order.returned.v1";
    public const string OrderRefunded = "order.refunded.v1";
}

public record IntegrationOrderLine(string Sku, int Quantity, decimal UnitPrice);

public record OrderConfirmedV1(Guid OrderId, string CustomerId, IReadOnlyList<IntegrationOrderLine> Lines, decimal Total, string Currency, DateTimeOffset ConfirmedAt);

public record OrderShippedV1(Guid OrderId, string CustomerId, DateTimeOffset ShippedAt);

public record OrderReturnedV1(Guid OrderId, string CustomerId, DateTimeOffset ReturnedAt, IReadOnlyList<IntegrationOrderLine> Lines);

public record OrderRefundedV1(Guid OrderId, string CustomerId, decimal Amount, string Currency, DateTimeOffset RefundedAt, string? Reason);
