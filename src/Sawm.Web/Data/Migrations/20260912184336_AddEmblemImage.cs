using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sawm.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddEmblemImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EmblemImageUrl",
                table: "ThemeSettings",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EmblemImageUrl",
                table: "ThemeSettings");
        }
    }
}
