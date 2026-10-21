using System.Diagnostics.Metrics;

namespace Orderflow.Observability;

/// <summary>Business metrics behind the "Orderflow saga flow" dashboard.</summary>
public static class OrderflowMetrics
{
    private static readonly Meter Meter = new(TelemetryExtensions.Name);

    public static readonly Counter<long> OrdersSubmitted =
        Meter.CreateCounter<long>("orderflow.orders.submitted", "{order}", "Orders accepted by the Orders API.");

    public static readonly Counter<long> OrdersCompleted =
        Meter.CreateCounter<long>("orderflow.orders.completed", "{order}", "Orders that finished the saga, by outcome (confirmed or rejected).");

    public static readonly Counter<long> PaymentsProcessed =
        Meter.CreateCounter<long>("orderflow.payments.processed", "{payment}", "Payment attempts, by result (completed or declined).");

    public static readonly Counter<long> StockReservations =
        Meter.CreateCounter<long>("orderflow.stock.reservations", "{reservation}", "Stock reservation attempts, by result (reserved or rejected).");

    public static readonly Histogram<double> OrderDuration =
        Meter.CreateHistogram<double>("orderflow.orders.duration", "s", "Time from submission to the final saga outcome.",
            tags: null,
            advice: new InstrumentAdvice<double> { HistogramBucketBoundaries = [0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10, 30] });
}
