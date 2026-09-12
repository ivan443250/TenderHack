using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderHack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddTurnRevisionUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_turns_case_id",
                table: "turns");

            migrationBuilder.CreateIndex(
                name: "ix_turns_case_id_revision",
                table: "turns",
                columns: new[] { "case_id", "revision" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_turns_case_id_revision",
                table: "turns");

            migrationBuilder.CreateIndex(
                name: "ix_turns_case_id",
                table: "turns",
                column: "case_id");
        }
    }
}
