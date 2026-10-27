using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orderflow.Orders.Migrations
{
    /// <inheritdoc />
    public partial class ReturnReceivedAndRepeatableTimeline : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_order_timeline_OrderId_Step",
                schema: "app",
                table: "order_timeline");

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ReturnReceivedAt",
                schema: "app",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_order_timeline_OrderId_Step",
                schema: "app",
                table: "order_timeline",
                columns: new[] { "OrderId", "Step" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_order_timeline_OrderId_Step",
                schema: "app",
                table: "order_timeline");

            migrationBuilder.DropColumn(
                name: "ReturnReceivedAt",
                schema: "app",
                table: "orders");

            migrationBuilder.CreateIndex(
                name: "IX_order_timeline_OrderId_Step",
                schema: "app",
                table: "order_timeline",
                columns: new[] { "OrderId", "Step" },
                unique: true);
        }
    }
}
