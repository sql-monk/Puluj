using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddKinematicLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "confidence",
                table: "target_links",
                newName: "probability");

            migrationBuilder.AddColumn<double>(
                name: "distance_km",
                table: "target_links",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "heading_diff_deg",
                table: "target_links",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "minutes_apart",
                table: "target_links",
                type: "double precision",
                nullable: true);

            migrationBuilder.AddColumn<double>(
                name: "required_minutes",
                table: "target_links",
                type: "double precision",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "source_copies",
                columns: table => new
                {
                    copier_source_id = table.Column<int>(type: "integer", nullable: false),
                    original_source_id = table.Column<int>(type: "integer", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    count = table.Column<int>(type: "integer", nullable: false),
                    delay_seconds_sum = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_copies", x => new { x.copier_source_id, x.original_source_id, x.day });
                });

            migrationBuilder.CreateTable(
                name: "source_daily_stats",
                columns: table => new
                {
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    targets = table.Column<int>(type: "integer", nullable: false),
                    copies = table.Column<int>(type: "integer", nullable: false),
                    copied_by = table.Column<int>(type: "integer", nullable: false),
                    lead_seconds_sum = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_source_daily_stats", x => new { x.source_id, x.day });
                });

            migrationBuilder.CreateIndex(
                name: "ix_source_copies_day",
                table: "source_copies",
                column: "day");

            migrationBuilder.CreateIndex(
                name: "ix_source_daily_stats_day",
                table: "source_daily_stats",
                column: "day");

            // Functions, triggers, view and backfill: the kinematic linker and source statistics live in the database.
            migrationBuilder.Sql(Sql.KinematicsSql.Up);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Sql.KinematicsSql.Down);
            migrationBuilder.DropTable(
                name: "source_copies");

            migrationBuilder.DropTable(
                name: "source_daily_stats");

            migrationBuilder.DropColumn(
                name: "distance_km",
                table: "target_links");

            migrationBuilder.DropColumn(
                name: "heading_diff_deg",
                table: "target_links");

            migrationBuilder.DropColumn(
                name: "minutes_apart",
                table: "target_links");

            migrationBuilder.DropColumn(
                name: "required_minutes",
                table: "target_links");

            migrationBuilder.RenameColumn(
                name: "probability",
                table: "target_links",
                newName: "confidence");
        }
    }
}
