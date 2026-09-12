using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TenderHack.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddNotificationSourceEventId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "source_event_id",
                table: "notifications",
                type: "bigint",
                nullable: false,
                defaultValue: 0L);

            // Rows from before this column existed have no source event; a shared default of 0 would
            // collide under the unique index below whenever one case already has several
            // HANDOFF_UPDATED notifications (the normal result of staged status updates). Negative
            // per-row values are unique and can never match a real case_events.id (always positive).
            migrationBuilder.Sql("UPDATE notifications SET source_event_id = -id;");

            migrationBuilder.CreateIndex(
                name: "ix_notifications_owner_id_case_id_type_source_event_id",
                table: "notifications",
                columns: new[] { "owner_id", "case_id", "type", "source_event_id" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_notifications_owner_id_case_id_type_source_event_id",
                table: "notifications");

            migrationBuilder.DropColumn(
                name: "source_event_id",
                table: "notifications");
        }
    }
}
