using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Puluj.Analytics.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialAnalytics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "analytics");

            migrationBuilder.CreateTable(
                name: "copies",
                schema: "analytics",
                columns: table => new
                {
                    copy_source_id = table.Column<int>(type: "integer", nullable: false),
                    copy_post_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    original_source_id = table.Column<int>(type: "integer", nullable: false),
                    original_post_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    copy_raw_message_id = table.Column<long>(type: "bigint", nullable: false),
                    original_raw_message_id = table.Column<long>(type: "bigint", nullable: false),
                    copy_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    original_published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    delay_seconds = table.Column<double>(type: "double precision", nullable: false),
                    jaccard = table.Column<float>(type: "real", nullable: false),
                    containment = table.Column<float>(type: "real", nullable: false),
                    kind = table.Column<int>(type: "integer", nullable: false),
                    is_primary = table.Column<bool>(type: "boolean", nullable: false),
                    found_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_copies", x => new { x.copy_source_id, x.copy_post_key, x.original_source_id, x.original_post_key });
                });

            migrationBuilder.CreateTable(
                name: "messages",
                schema: "analytics",
                columns: table => new
                {
                    raw_message_id = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    post_key = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    is_edit = table.Column<bool>(type: "boolean", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    channel_id = table.Column<long>(type: "bigint", nullable: true),
                    forwarded_from = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    forwarded_source_id = table.Column<int>(type: "integer", nullable: true),
                    forwarded_external = table.Column<bool>(type: "boolean", nullable: false),
                    text_length = table.Column<int>(type: "integer", nullable: false),
                    shingle_count = table.Column<int>(type: "integer", nullable: false),
                    min_hash = table.Column<byte[]>(type: "bytea", nullable: true),
                    bands = table.Column<long[]>(type: "bigint[]", nullable: true),
                    indexed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_messages", x => x.raw_message_id);
                });

            migrationBuilder.CreateTable(
                name: "runs",
                schema: "analytics",
                columns: table => new
                {
                    run_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    instance = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    status = table.Column<int>(type: "integer", nullable: false),
                    watermark_from = table.Column<long>(type: "bigint", nullable: false),
                    watermark_to = table.Column<long>(type: "bigint", nullable: false),
                    messages_scanned = table.Column<int>(type: "integer", nullable: false),
                    messages_fingerprinted = table.Column<int>(type: "integer", nullable: false),
                    pairs_found = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_runs", x => x.run_id);
                });

            migrationBuilder.CreateTable(
                name: "state",
                schema: "analytics",
                columns: table => new
                {
                    key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    value = table.Column<string>(type: "text", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_state", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "track_firsts",
                schema: "analytics",
                columns: table => new
                {
                    day = table.Column<DateOnly>(type: "date", nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    target_category_id = table.Column<int>(type: "integer", nullable: false),
                    category_code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    firsts = table.Column<int>(type: "integer", nullable: false),
                    participations = table.Column<int>(type: "integer", nullable: false),
                    lag_seconds_sum = table.Column<double>(type: "double precision", nullable: false),
                    lag_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_firsts", x => new { x.day, x.source_id, x.target_category_id });
                });

            migrationBuilder.CreateIndex(
                name: "ix_copies_copy_published_at",
                schema: "analytics",
                table: "copies",
                column: "copy_published_at");

            migrationBuilder.CreateIndex(
                name: "ix_copies_copy_source_id_original_source_id_copy_published_at",
                schema: "analytics",
                table: "copies",
                columns: new[] { "copy_source_id", "original_source_id", "copy_published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_copies_original_raw_message_id",
                schema: "analytics",
                table: "copies",
                column: "original_raw_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_messages_bands",
                schema: "analytics",
                table: "messages",
                column: "bands",
                filter: "bands IS NOT NULL")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_messages_published_at",
                schema: "analytics",
                table: "messages",
                column: "published_at");

            migrationBuilder.CreateIndex(
                name: "ix_messages_source_id_channel_id",
                schema: "analytics",
                table: "messages",
                columns: new[] { "source_id", "channel_id" });

            migrationBuilder.CreateIndex(
                name: "ix_messages_source_id_published_at",
                schema: "analytics",
                table: "messages",
                columns: new[] { "source_id", "published_at" });

            migrationBuilder.CreateIndex(
                name: "ix_runs_started_at",
                schema: "analytics",
                table: "runs",
                column: "started_at",
                descending: new bool[0]);

            // The admin panel (puluj_admin) reads the schema and resets it; the public Api role may read it. Default
            // privileges make the grants stick to tables added by later migrations. Skipped when the roles do not exist
            // (a database without the pipeline's migrations).
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'puluj_admin') THEN
                        GRANT USAGE ON SCHEMA analytics TO puluj_admin;
                        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA analytics TO puluj_admin;
                        GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA analytics TO puluj_admin;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA analytics GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO puluj_admin;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA analytics GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO puluj_admin;
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'puluj_reader') THEN
                        GRANT USAGE ON SCHEMA analytics TO puluj_reader;
                        GRANT SELECT ON ALL TABLES IN SCHEMA analytics TO puluj_reader;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA analytics GRANT SELECT ON TABLES TO puluj_reader;
                    END IF;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'puluj_admin') THEN
                        ALTER DEFAULT PRIVILEGES IN SCHEMA analytics REVOKE ALL ON TABLES FROM puluj_admin;
                        ALTER DEFAULT PRIVILEGES IN SCHEMA analytics REVOKE ALL ON SEQUENCES FROM puluj_admin;
                        REVOKE ALL ON ALL TABLES IN SCHEMA analytics FROM puluj_admin;
                        REVOKE ALL ON ALL SEQUENCES IN SCHEMA analytics FROM puluj_admin;
                        REVOKE USAGE ON SCHEMA analytics FROM puluj_admin;
                    END IF;
                    IF EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'puluj_reader') THEN
                        ALTER DEFAULT PRIVILEGES IN SCHEMA analytics REVOKE ALL ON TABLES FROM puluj_reader;
                        REVOKE ALL ON ALL TABLES IN SCHEMA analytics FROM puluj_reader;
                        REVOKE USAGE ON SCHEMA analytics FROM puluj_reader;
                    END IF;
                END $$;
                """);

            migrationBuilder.DropTable(
                name: "copies",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "messages",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "runs",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "state",
                schema: "analytics");

            migrationBuilder.DropTable(
                name: "track_firsts",
                schema: "analytics");
        }
    }
}
