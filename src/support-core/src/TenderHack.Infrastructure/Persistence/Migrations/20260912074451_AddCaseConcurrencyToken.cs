using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderHack.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Registers Postgres's built-in `xmin` system column as the `Case` concurrency token
    /// (architecture.md §7). `xmin` already exists on every table — there is nothing to add or drop;
    /// this migration exists only so EF's model snapshot records the mapping.
    /// </summary>
    public partial class AddCaseConcurrencyToken : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
