using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations;

/// <summary>
/// target_anchors: the (simplified) anchor of every located target, computed once at insert. The linker joins it
/// instead of rebuilding every candidate's oblast polygon per new target, which made linking cost seconds on busy
/// nights. Not an EF entity: the database owns it (cascade from targets).
/// </summary>
[DbContext(typeof(PulujDbContext))]
[Migration("20260913190000_AddTargetAnchors")]
public partial class AddTargetAnchors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(Sql.KinematicsSql.Anchors);
        migrationBuilder.Sql(Sql.KinematicsSql.LinkTarget);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The anchors table goes; the trigger and linker keep their newer text (they would fail without the table).
        migrationBuilder.Sql("DROP TABLE IF EXISTS target_anchors;");
    }
}
