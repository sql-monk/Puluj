using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Processor claims: several processor instances share raw_messages, a claim marks a row InProgress (status 4) with
    /// the instance name and time; claims older than the lease are returned to Pending by whichever instance sweeps first.
    /// </summary>
    public partial class AddRawMessageClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "claimed_at",
                table: "raw_messages",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "claimed_by",
                table: "raw_messages",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_messages_in_progress_claimed_at",
                table: "raw_messages",
                column: "claimed_at",
                filter: "processing_status = 4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_raw_messages_in_progress_claimed_at",
                table: "raw_messages");

            migrationBuilder.DropColumn(
                name: "claimed_at",
                table: "raw_messages");

            migrationBuilder.DropColumn(
                name: "claimed_by",
                table: "raw_messages");
        }
    }
}
