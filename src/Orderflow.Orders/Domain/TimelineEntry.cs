namespace Orderflow.Orders.Domain;

/// <summary>Read-model row: one step of an order's saga history.</summary>
public class TimelineEntry
{
    public int Id { get; set; }
    public Guid OrderId { get; set; }
    public string Step { get; set; } = null!;
    public string? Detail { get; set; }
    public DateTimeOffset At { get; set; }
}
