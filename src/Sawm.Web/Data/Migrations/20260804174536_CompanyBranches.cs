using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sawm.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompanyBranches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "BidLimit",
                table: "CompanyProfiles",
                type: "decimal(14,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CanBid",
                table: "CompanyProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanCreateTenders",
                table: "CompanyProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanManageContracts",
                table: "CompanyProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "CanSubmitOffers",
                table: "CompanyProfiles",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ParentCompanyId",
                table: "CompanyProfiles",
                type: "nvarchar(450)",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Bids",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedById",
                table: "Bids",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TotalValueAtBid",
                table: "Bids",
                type: "decimal(14,2)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.CreateIndex(
                name: "IX_CompanyProfiles_ParentCompanyId",
                table: "CompanyProfiles",
                column: "ParentCompanyId");

            migrationBuilder.AddForeignKey(
                name: "FK_CompanyProfiles_AspNetUsers_ParentCompanyId",
                table: "CompanyProfiles",
                column: "ParentCompanyId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_CompanyProfiles_AspNetUsers_ParentCompanyId",
                table: "CompanyProfiles");

            migrationBuilder.DropIndex(
                name: "IX_CompanyProfiles_ParentCompanyId",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "BidLimit",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "CanBid",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "CanCreateTenders",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "CanManageContracts",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "CanSubmitOffers",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "ParentCompanyId",
                table: "CompanyProfiles");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "Bids");

            migrationBuilder.DropColumn(
                name: "TotalValueAtBid",
                table: "Bids");
        }
    }
}
