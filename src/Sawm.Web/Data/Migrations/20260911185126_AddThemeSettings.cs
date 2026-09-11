using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Sawm.Web.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddThemeSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ThemeSettings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Primary = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Secondary = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    Accent = table.Column<string>(type: "nvarchar(9)", maxLength: 9, nullable: false),
                    RadiusCard = table.Column<int>(type: "int", nullable: false),
                    RadiusButton = table.Column<int>(type: "int", nullable: false),
                    RadiusIcon = table.Column<int>(type: "int", nullable: false),
                    RadiusControl = table.Column<int>(type: "int", nullable: false),
                    IconScale = table.Column<int>(type: "int", nullable: false),
                    GlassOpacity = table.Column<int>(type: "int", nullable: false),
                    GlassBlur = table.Column<int>(type: "int", nullable: false),
                    HeaderImageUrl = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    BgEffect = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ThemeSettings", x => x.Id);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ThemeSettings");
        }
    }
}
