using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orderflow.Payments.Migrations
{
    /// <inheritdoc />
    public partial class MarketplaceLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "RefundedAmount",
                schema: "app",
                table: "payments",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RefundedAt",
                schema: "app",
                table: "payments",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RefundedAmount",
                schema: "app",
                table: "payments");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                schema: "app",
                table: "payments");
        }
    }
}
