using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddAirAlertMessageIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "ix_air_alerts_end_raw_message_id",
                table: "air_alerts",
                column: "end_raw_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_air_alerts_start_raw_message_id",
                table: "air_alerts",
                column: "start_raw_message_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_air_alerts_end_raw_message_id",
                table: "air_alerts");

            migrationBuilder.DropIndex(
                name: "ix_air_alerts_start_raw_message_id",
                table: "air_alerts");
        }
    }
}
