using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sawm.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeFxFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FxColor",
                table: "ThemeSettings",
                type: "nvarchar(9)",
                maxLength: 9,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FxCount",
                table: "ThemeSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "FxShape",
                table: "ThemeSettings",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "FxSize",
                table: "ThemeSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "FxSpeed",
                table: "ThemeSettings",
                type: "int",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FxColor",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "FxCount",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "FxShape",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "FxSize",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "FxSpeed",
                table: "ThemeSettings");
        }
    }
}
