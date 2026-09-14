using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations;

/// <summary>
/// Linker v2: a distance prior, dwell for repeats in the same area, the candidate's remaining "future" (one object
/// has one continuation), a relative cut-off. Existing links are not recomputed here: run scripts/reprocess.sql (or
/// the admin "reprocess") to rebuild everything from the raw messages in order.
/// </summary>
[DbContext(typeof(PulujDbContext))]
[Migration("20260913170000_UpdateKinematicLinker")]
public partial class UpdateKinematicLinker : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Sql.KinematicsSql.LinkTarget);

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // The previous linker is not restored; the function stays (the v1 text lives in git history).
    }
}
