using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderHack.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Registers Postgres's built-in `xmin` system column as the `turns` row's own concurrency
    /// token (architecture.md §6, §7 — C2: "running old turn cannot publish after a newer revision
    /// supersedes it" needs enforcement at the `turns` row itself, since a long-running turn's final
    /// `UPDATE` never touches the `cases` row and so never trips that table's own `xmin` check).
    /// `xmin` already exists on every table — there is nothing to add or drop; this migration exists
    /// only so EF's model snapshot records the mapping (same pattern as AddCaseConcurrencyToken).
    /// </summary>
    public partial class AddTurnsConcurrencyToken : Migration
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
