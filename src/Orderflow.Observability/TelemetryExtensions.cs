using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using OpenTelemetry;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace Orderflow.Observability;

public static class TelemetryExtensions
{
    /// <summary>The meter and activity source for Orderflow's own business telemetry.</summary>
    public const string Name = "Orderflow";

    /// <summary>
    /// Traces, metrics and logs for one service. MassTransit already emits spans and metrics for every
    /// publish and consume, and the W3C context travels in message headers, so one order is a single
    /// trace from the gateway through RabbitMQ to every service.
    /// Export goes over OTLP only when OTEL_EXPORTER_OTLP_ENDPOINT is set, so a bare run stays quiet.
    /// </summary>
    public static WebApplicationBuilder AddOrderflowTelemetry(this WebApplicationBuilder builder, string serviceName)
    {
        builder.Logging.AddOpenTelemetry(o =>
        {
            o.IncludeFormattedMessage = true;
            o.IncludeScopes = true;
        });

        var otel = builder.Services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService(serviceName))
            .WithTracing(t => t
                .AddSource(Name, "MassTransit")
                .AddAspNetCoreInstrumentation(o => o.Filter = ctx => ctx.Request.Path != "/health")
                .AddHttpClientInstrumentation()
                .AddNpgsql())
            .WithMetrics(m => m
                .AddMeter(Name, "MassTransit")
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation()
                .AddNpgsqlInstrumentation());

        if (!string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]))
            otel.UseOtlpExporter();

        return builder;
    }
}
