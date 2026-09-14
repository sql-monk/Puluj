using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations;

/// <summary>Pending raw messages by publication time: the sweep hands them over oldest-published first; the watchdog asks for the oldest one.</summary>
[DbContext(typeof(PulujDbContext))]
[Migration("20260913180000_AddPendingPublishedIndex")]
public partial class AddPendingPublishedIndex : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder) =>
        migrationBuilder.CreateIndex(name: "ix_raw_messages_pending_published", table: "raw_messages", column: "published_at", filter: "processing_status = 0");

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropIndex(name: "ix_raw_messages_pending_published", table: "raw_messages");
}
