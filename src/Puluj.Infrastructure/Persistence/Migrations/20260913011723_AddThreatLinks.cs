using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddThreatLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "threat_links",
                columns: table => new
                {
                    from_threat_id = table.Column<long>(type: "bigint", nullable: false),
                    to_threat_id = table.Column<long>(type: "bigint", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    confidence = table.Column<double>(type: "double precision", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_links", x => new { x.from_threat_id, x.to_threat_id });
                    table.ForeignKey(
                        name: "fk_threat_links_threats_from_threat_id",
                        column: x => x.from_threat_id,
                        principalTable: "threats",
                        principalColumn: "threat_id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_threat_links_threats_to_threat_id",
                        column: x => x.to_threat_id,
                        principalTable: "threats",
                        principalColumn: "threat_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_threat_links_created_at",
                table: "threat_links",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_threat_links_to_threat_id",
                table: "threat_links",
                column: "to_threat_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "threat_links");
        }
    }
}
