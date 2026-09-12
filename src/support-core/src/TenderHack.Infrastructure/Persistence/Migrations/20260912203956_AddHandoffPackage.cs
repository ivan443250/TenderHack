using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderHack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddHandoffPackage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "confirmed_summary",
                table: "handoffs",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "package_json",
                table: "handoffs",
                type: "text",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "confirmed_summary",
                table: "handoffs");

            migrationBuilder.DropColumn(
                name: "package_json",
                table: "handoffs");
        }
    }
}
