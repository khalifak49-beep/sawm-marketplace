using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sawm.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeImageUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "EmblemImageData",
                table: "ThemeSettings",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EmblemImageType",
                table: "ThemeSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "HeaderImageData",
                table: "ThemeSettings",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HeaderImageType",
                table: "ThemeSettings",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmblemImageData",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "EmblemImageType",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "HeaderImageData",
                table: "ThemeSettings");

            migrationBuilder.DropColumn(
                name: "HeaderImageType",
                table: "ThemeSettings");
        }
    }
}
