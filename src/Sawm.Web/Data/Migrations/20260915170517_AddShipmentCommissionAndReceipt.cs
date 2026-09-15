using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sawm.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddShipmentCommissionAndReceipt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "ShippingCommissionRate",
                table: "Contracts",
                type: "decimal(5,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "ShippingReceived",
                table: "Contracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippingReceivedAt",
                table: "Contracts",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ShippingReceivedBy",
                table: "Contracts",
                type: "nvarchar(120)",
                maxLength: 120,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingCommissionRate",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ShippingReceived",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ShippingReceivedAt",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ShippingReceivedBy",
                table: "Contracts");
        }
    }
}
