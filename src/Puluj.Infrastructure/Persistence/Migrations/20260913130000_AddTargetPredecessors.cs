using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations;

/// <summary>Read function puluj_target_predecessors: all probable predecessors of a target, a few generations deep.</summary>
[DbContext(typeof(PulujDbContext))]
[Migration("20260913130000_AddTargetPredecessors")]
public partial class AddTargetPredecessors : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Sql.PredecessorsSql.Up);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Sql.PredecessorsSql.Down);
}
