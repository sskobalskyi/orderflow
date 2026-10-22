using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Orderflow.Orders.Migrations
{
    /// <inheritdoc />
    public partial class DeviceClaimedActivation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ActivatedAt",
                schema: "app",
                table: "orders",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "integration_inbox",
                schema: "app",
                columns: table => new
                {
                    MessageId = table.Column<Guid>(type: "uuid", nullable: false),
                    Consumer = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_integration_inbox", x => new { x.MessageId, x.Consumer });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "integration_inbox",
                schema: "app");

            migrationBuilder.DropColumn(
                name: "ActivatedAt",
                schema: "app",
                table: "orders");
        }
    }
}
