using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations;

/// <summary>Btree indexes for the two hot range scans: the linker's candidates (category + observed_at) and the correlator's candidate tracks (last_seen_at).</summary>
[DbContext(typeof(PulujDbContext))]
[Migration("20260913200000_AddLinkerScanIndexes")]
public partial class AddLinkerScanIndexes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(name: "ix_targets_target_category_id_observed_at", table: "targets", columns: ["target_category_id", "observed_at"]);
        // The composite index serves the foreign-key lookups the single-column one did.
        migrationBuilder.DropIndex(name: "ix_targets_target_category_id", table: "targets");
        migrationBuilder.CreateIndex(name: "ix_target_tracks_last_seen_at", table: "target_tracks", column: "last_seen_at");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(name: "ix_targets_target_category_id", table: "targets", column: "target_category_id");
        migrationBuilder.DropIndex(name: "ix_targets_target_category_id_observed_at", table: "targets");
        migrationBuilder.DropIndex(name: "ix_target_tracks_last_seen_at", table: "target_tracks");
    }
}
