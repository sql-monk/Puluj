using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations;

/// <summary>Read function puluj_target_family: a target's ancestors and where else those ancestors could have flown.</summary>
[DbContext(typeof(PulujDbContext))]
[Migration("20260913150000_AddTargetFamily")]
public partial class AddTargetFamily : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Sql.PredecessorsSql.FamilyUp);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(Sql.PredecessorsSql.FamilyDown);
}
