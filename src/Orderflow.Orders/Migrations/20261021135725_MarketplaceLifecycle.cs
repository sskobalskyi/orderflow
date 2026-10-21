using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orderflow.Orders.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ConfirmedAt",
                schema: "app",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                schema: "app",
                table: "orders",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "USD");

            migrationBuilder.AddColumn<decimal>(
                name: "RefundAmount",
                schema: "app",
                table: "orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefundReason",
                schema: "app",
                table: "orders",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                schema: "app",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnedAt",
                schema: "app",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ShippedAt",
                schema: "app",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "integration_outbox",
                schema: "app",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoutingKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Body = table.Column<string>(type: "text", nullable: false),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Traceparent = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    PublishedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_outbox", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_integration_outbox_PublishedAt",
                schema: "app",
                table: "integration_outbox",
                column: "PublishedAt",
                filter: "\"PublishedAt\" IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_outbox",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "ConfirmedAt",
                schema: "app",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "Currency",
                schema: "app",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "RefundAmount",
                schema: "app",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "RefundReason",
                schema: "app",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                schema: "app",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ReturnedAt",
                schema: "app",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "ShippedAt",
                schema: "app",
                table: "orders");
        }
    }
}
