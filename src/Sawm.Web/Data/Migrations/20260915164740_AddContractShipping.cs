using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sawm.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddContractShipping : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShippingReleased",
                table: "Contracts",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "ShippingReleasedAt",
                table: "Contracts",
                type: "datetime2",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ShippingReleased",
                table: "Contracts");

            migrationBuilder.DropColumn(
                name: "ShippingReleasedAt",
                table: "Contracts");
        }
    }
}
