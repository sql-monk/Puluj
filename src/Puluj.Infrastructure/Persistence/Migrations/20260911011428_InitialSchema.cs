using System;
using System.Text.Json;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "places",
                columns: table => new
                {
                    place_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    name_variants = table.Column<string[]>(type: "text[]", nullable: false),
                    level = table.Column<int>(type: "integer", nullable: false),
                    parent_id = table.Column<int>(type: "integer", nullable: true),
                    country_code = table.Column<string>(type: "character varying(2)", maxLength: 2, nullable: false),
                    katottg_code = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    external_key = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    population = table.Column<int>(type: "integer", nullable: true),
                    geometry = table.Column<Geometry>(type: "geometry (geometry, 4326)", nullable: false),
                    centroid = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    radius_km = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_places", x => x.place_id);
                    table.ForeignKey(
                        name: "fk_places_places_parent_id",
                        column: x => x.parent_id,
                        principalTable: "places",
                        principalColumn: "place_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "processing_errors",
                columns: table => new
                {
                    processing_error_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_message_id = table.Column<long>(type: "bigint", nullable: true),
                    source_id = table.Column<int>(type: "integer", nullable: true),
                    stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    message = table.Column<string>(type: "text", nullable: false),
                    exception = table.Column<string>(type: "text", nullable: true),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    payload = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_processing_errors", x => x.processing_error_id);
                });

            migrationBuilder.CreateTable(
                name: "sources",
                columns: table => new
                {
                    source_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<int>(type: "integer", nullable: false),
                    url = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    trust_level = table.Column<double>(type: "double precision", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    polling_interval = table.Column<TimeSpan>(type: "interval", nullable: true),
                    config = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_sources", x => x.source_id);
                });

            migrationBuilder.CreateTable(
                name: "threat_categories",
                columns: table => new
                {
                    threat_category_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_categories", x => x.threat_category_id);
                });

            migrationBuilder.CreateTable(
                name: "threat_model_aliases",
                columns: table => new
                {
                    threat_model_alias_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    alias = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    language = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: false),
                    target_level = table.Column<int>(type: "integer", nullable: false),
                    target_id = table.Column<int>(type: "integer", nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: true),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    exact_match = table.Column<bool>(type: "boolean", nullable: false),
                    implied_confidence = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_model_aliases", x => x.threat_model_alias_id);
                });

            migrationBuilder.CreateTable(
                name: "user_locations",
                columns: table => new
                {
                    user_location_id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    location = table.Column<Point>(type: "geography (point, 4326)", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_user_locations", x => x.user_location_id);
                });

            migrationBuilder.CreateTable(
                name: "air_alerts",
                columns: table => new
                {
                    air_alert_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    place_id = table.Column<int>(type: "integer", nullable: false),
                    alert_type = table.Column<int>(type: "integer", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    start_raw_message_id = table.Column<long>(type: "bigint", nullable: true),
                    end_raw_message_id = table.Column<long>(type: "bigint", nullable: true),
                    source_alert_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_air_alerts", x => x.air_alert_id);
                    table.ForeignKey(
                        name: "fk_air_alerts_places_place_id",
                        column: x => x.place_id,
                        principalTable: "places",
                        principalColumn: "place_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_air_alerts_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "source_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "collector_states",
                columns: table => new
                {
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    last_source_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    last_polled_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_success_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_message_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    last_error = table.Column<string>(type: "text", nullable: true),
                    consecutive_failures = table.Column<int>(type: "integer", nullable: false),
                    cursor = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_collector_states", x => x.source_id);
                    table.ForeignKey(
                        name: "fk_collector_states_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "source_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "raw_messages",
                columns: table => new
                {
                    raw_message_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    source_message_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    received_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    raw_text = table.Column<string>(type: "text", nullable: true),
                    raw_payload = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    processing_status = table.Column<int>(type: "integer", nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_raw_messages", x => x.raw_message_id);
                    table.ForeignKey(
                        name: "fk_raw_messages_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "source_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "threat_classes",
                columns: table => new
                {
                    threat_class_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    threat_category_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_classes", x => x.threat_class_id);
                    table.ForeignKey(
                        name: "fk_threat_classes_threat_categories_threat_category_id",
                        column: x => x.threat_category_id,
                        principalTable: "threat_categories",
                        principalColumn: "threat_category_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "threat_families",
                columns: table => new
                {
                    threat_family_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    threat_class_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_families", x => x.threat_family_id);
                    table.ForeignKey(
                        name: "fk_threat_families_threat_classes_threat_class_id",
                        column: x => x.threat_class_id,
                        principalTable: "threat_classes",
                        principalColumn: "threat_class_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "threat_models",
                columns: table => new
                {
                    threat_model_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    threat_family_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    canonical_name = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    manufacturer = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    country = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                    enabled = table.Column<bool>(type: "boolean", nullable: false),
                    metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_models", x => x.threat_model_id);
                    table.ForeignKey(
                        name: "fk_threat_models_threat_families_threat_family_id",
                        column: x => x.threat_family_id,
                        principalTable: "threat_families",
                        principalColumn: "threat_family_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "observations",
                columns: table => new
                {
                    observation_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    raw_message_id = table.Column<long>(type: "bigint", nullable: false),
                    source_id = table.Column<int>(type: "integer", nullable: false),
                    segment_index = table.Column<int>(type: "integer", nullable: false),
                    observed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    event_type = table.Column<int>(type: "integer", nullable: false),
                    threat_category_id = table.Column<int>(type: "integer", nullable: true),
                    threat_class_id = table.Column<int>(type: "integer", nullable: true),
                    threat_family_id = table.Column<int>(type: "integer", nullable: true),
                    threat_model_id = table.Column<int>(type: "integer", nullable: true),
                    model_confidence = table.Column<int>(type: "integer", nullable: false),
                    classification_confidence = table.Column<int>(type: "integer", nullable: false),
                    identification_method = table.Column<int>(type: "integer", nullable: false),
                    identification_source = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    object_count = table.Column<int>(type: "integer", nullable: true),
                    object_count_is_approximate = table.Column<bool>(type: "boolean", nullable: false),
                    location_kind = table.Column<int>(type: "integer", nullable: false),
                    location_place_id = table.Column<int>(type: "integer", nullable: true),
                    location = table.Column<Geometry>(type: "geography (geometry, 4326)", nullable: true),
                    location_accuracy_km = table.Column<double>(type: "double precision", nullable: true),
                    origin_place_id = table.Column<int>(type: "integer", nullable: true),
                    destination_place_id = table.Column<int>(type: "integer", nullable: true),
                    direction_kind = table.Column<int>(type: "integer", nullable: false),
                    direction_deg = table.Column<double>(type: "double precision", nullable: true),
                    direction_confidence = table.Column<int>(type: "integer", nullable: false),
                    observation_confidence = table.Column<int>(type: "integer", nullable: false),
                    duplicate_of_observation_id = table.Column<long>(type: "bigint", nullable: true),
                    parser_version = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    parser_metadata = table.Column<JsonDocument>(type: "jsonb", nullable: true),
                    segment_text = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_observations", x => x.observation_id);
                    table.ForeignKey(
                        name: "fk_observations_observations_duplicate_of_observation_id",
                        column: x => x.duplicate_of_observation_id,
                        principalTable: "observations",
                        principalColumn: "observation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_places_destination_place_id",
                        column: x => x.destination_place_id,
                        principalTable: "places",
                        principalColumn: "place_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_places_location_place_id",
                        column: x => x.location_place_id,
                        principalTable: "places",
                        principalColumn: "place_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_places_origin_place_id",
                        column: x => x.origin_place_id,
                        principalTable: "places",
                        principalColumn: "place_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_raw_messages_raw_message_id",
                        column: x => x.raw_message_id,
                        principalTable: "raw_messages",
                        principalColumn: "raw_message_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_sources_source_id",
                        column: x => x.source_id,
                        principalTable: "sources",
                        principalColumn: "source_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_threat_categories_threat_category_id",
                        column: x => x.threat_category_id,
                        principalTable: "threat_categories",
                        principalColumn: "threat_category_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_threat_classes_threat_class_id",
                        column: x => x.threat_class_id,
                        principalTable: "threat_classes",
                        principalColumn: "threat_class_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_threat_families_threat_family_id",
                        column: x => x.threat_family_id,
                        principalTable: "threat_families",
                        principalColumn: "threat_family_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_observations_threat_models_threat_model_id",
                        column: x => x.threat_model_id,
                        principalTable: "threat_models",
                        principalColumn: "threat_model_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "threat_tracks",
                columns: table => new
                {
                    threat_track_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    status = table.Column<int>(type: "integer", nullable: false),
                    closed_reason = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    threat_category_id = table.Column<int>(type: "integer", nullable: false),
                    threat_class_id = table.Column<int>(type: "integer", nullable: true),
                    threat_family_id = table.Column<int>(type: "integer", nullable: true),
                    threat_model_id = table.Column<int>(type: "integer", nullable: true),
                    model_confidence = table.Column<int>(type: "integer", nullable: false),
                    first_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_location_kind = table.Column<int>(type: "integer", nullable: false),
                    last_location_place_id = table.Column<int>(type: "integer", nullable: true),
                    last_location = table.Column<Geometry>(type: "geography (geometry, 4326)", nullable: true),
                    last_location_accuracy_km = table.Column<double>(type: "double precision", nullable: true),
                    track_geometry = table.Column<LineString>(type: "geometry (linestring, 4326)", nullable: true),
                    direction_kind = table.Column<int>(type: "integer", nullable: false),
                    direction_deg = table.Column<double>(type: "double precision", nullable: true),
                    direction_confidence = table.Column<int>(type: "integer", nullable: false),
                    object_count = table.Column<int>(type: "integer", nullable: true),
                    track_confidence = table.Column<int>(type: "integer", nullable: false),
                    observation_count = table.Column<int>(type: "integer", nullable: false),
                    distinct_source_count = table.Column<int>(type: "integer", nullable: false),
                    last_source_id = table.Column<int>(type: "integer", nullable: true),
                    last_observation_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_tracks", x => x.threat_track_id);
                    table.ForeignKey(
                        name: "fk_threat_tracks_places_last_location_place_id",
                        column: x => x.last_location_place_id,
                        principalTable: "places",
                        principalColumn: "place_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_threat_tracks_threat_categories_threat_category_id",
                        column: x => x.threat_category_id,
                        principalTable: "threat_categories",
                        principalColumn: "threat_category_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_threat_tracks_threat_classes_threat_class_id",
                        column: x => x.threat_class_id,
                        principalTable: "threat_classes",
                        principalColumn: "threat_class_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_threat_tracks_threat_families_threat_family_id",
                        column: x => x.threat_family_id,
                        principalTable: "threat_families",
                        principalColumn: "threat_family_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_threat_tracks_threat_models_threat_model_id",
                        column: x => x.threat_model_id,
                        principalTable: "threat_models",
                        principalColumn: "threat_model_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "threat_track_observations",
                columns: table => new
                {
                    threat_track_id = table.Column<long>(type: "bigint", nullable: false),
                    observation_id = table.Column<long>(type: "bigint", nullable: false),
                    sequence = table.Column<int>(type: "integer", nullable: false),
                    association_confidence = table.Column<double>(type: "double precision", nullable: false),
                    association_reason = table.Column<JsonDocument>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_track_observations", x => new { x.threat_track_id, x.observation_id });
                    table.ForeignKey(
                        name: "fk_threat_track_observations_observations_observation_id",
                        column: x => x.observation_id,
                        principalTable: "observations",
                        principalColumn: "observation_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_threat_track_observations_threat_tracks_threat_track_id",
                        column: x => x.threat_track_id,
                        principalTable: "threat_tracks",
                        principalColumn: "threat_track_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "threat_track_revisions",
                columns: table => new
                {
                    threat_track_revision_id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    threat_track_id = table.Column<long>(type: "bigint", nullable: false),
                    revision_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    status = table.Column<int>(type: "integer", nullable: false),
                    threat_category_id = table.Column<int>(type: "integer", nullable: false),
                    threat_class_id = table.Column<int>(type: "integer", nullable: true),
                    threat_family_id = table.Column<int>(type: "integer", nullable: true),
                    threat_model_id = table.Column<int>(type: "integer", nullable: true),
                    model_confidence = table.Column<int>(type: "integer", nullable: false),
                    last_seen_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_location_kind = table.Column<int>(type: "integer", nullable: false),
                    last_location_place_id = table.Column<int>(type: "integer", nullable: true),
                    last_location = table.Column<Geometry>(type: "geography (geometry, 4326)", nullable: true),
                    last_location_accuracy_km = table.Column<double>(type: "double precision", nullable: true),
                    track_geometry = table.Column<LineString>(type: "geometry (linestring, 4326)", nullable: true),
                    direction_kind = table.Column<int>(type: "integer", nullable: false),
                    direction_deg = table.Column<double>(type: "double precision", nullable: true),
                    direction_confidence = table.Column<int>(type: "integer", nullable: false),
                    object_count = table.Column<int>(type: "integer", nullable: true),
                    track_confidence = table.Column<int>(type: "integer", nullable: false),
                    observation_count = table.Column<int>(type: "integer", nullable: false),
                    observation_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_threat_track_revisions", x => x.threat_track_revision_id);
                    table.ForeignKey(
                        name: "fk_threat_track_revisions_threat_tracks_threat_track_id",
                        column: x => x.threat_track_id,
                        principalTable: "threat_tracks",
                        principalColumn: "threat_track_id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_air_alerts_place_id",
                table: "air_alerts",
                column: "place_id",
                filter: "ended_at IS NULL");

            migrationBuilder.CreateIndex(
                name: "ix_air_alerts_source_id_source_alert_id",
                table: "air_alerts",
                columns: new[] { "source_id", "source_alert_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_air_alerts_started_at",
                table: "air_alerts",
                column: "started_at")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "ix_observations_destination_place_id",
                table: "observations",
                column: "destination_place_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_duplicate_of_observation_id",
                table: "observations",
                column: "duplicate_of_observation_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_location",
                table: "observations",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_observations_location_place_id",
                table: "observations",
                column: "location_place_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_observed_at",
                table: "observations",
                column: "observed_at")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "ix_observations_origin_place_id",
                table: "observations",
                column: "origin_place_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_raw_message_id",
                table: "observations",
                column: "raw_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_source_id",
                table: "observations",
                column: "source_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_threat_category_id",
                table: "observations",
                column: "threat_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_threat_class_id_observed_at",
                table: "observations",
                columns: new[] { "threat_class_id", "observed_at" });

            migrationBuilder.CreateIndex(
                name: "ix_observations_threat_family_id",
                table: "observations",
                column: "threat_family_id");

            migrationBuilder.CreateIndex(
                name: "ix_observations_threat_model_id",
                table: "observations",
                column: "threat_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_places_centroid",
                table: "places",
                column: "centroid")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_places_external_key",
                table: "places",
                column: "external_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_places_geometry",
                table: "places",
                column: "geometry")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_places_katottg_code",
                table: "places",
                column: "katottg_code",
                unique: true,
                filter: "katottg_code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_places_level_country_code",
                table: "places",
                columns: new[] { "level", "country_code" });

            migrationBuilder.CreateIndex(
                name: "ix_places_name_variants",
                table: "places",
                column: "name_variants")
                .Annotation("Npgsql:IndexMethod", "gin");

            migrationBuilder.CreateIndex(
                name: "ix_places_parent_id",
                table: "places",
                column: "parent_id");

            migrationBuilder.CreateIndex(
                name: "ix_processing_errors_occurred_at",
                table: "processing_errors",
                column: "occurred_at");

            migrationBuilder.CreateIndex(
                name: "ix_processing_errors_raw_message_id",
                table: "processing_errors",
                column: "raw_message_id");

            migrationBuilder.CreateIndex(
                name: "ix_raw_messages_hash",
                table: "raw_messages",
                column: "hash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_raw_messages_processing_status",
                table: "raw_messages",
                column: "processing_status",
                filter: "processing_status = 0");

            migrationBuilder.CreateIndex(
                name: "ix_raw_messages_published_at",
                table: "raw_messages",
                column: "published_at")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "ix_raw_messages_received_at",
                table: "raw_messages",
                column: "received_at")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "ix_raw_messages_source_id_source_message_id",
                table: "raw_messages",
                columns: new[] { "source_id", "source_message_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_sources_code",
                table: "sources",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_threat_categories_code",
                table: "threat_categories",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_threat_classes_code",
                table: "threat_classes",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_threat_classes_threat_category_id",
                table: "threat_classes",
                column: "threat_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_threat_families_code",
                table: "threat_families",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_threat_families_threat_class_id",
                table: "threat_families",
                column: "threat_class_id");

            migrationBuilder.CreateIndex(
                name: "ix_threat_model_aliases_alias_language_target_level_target_id",
                table: "threat_model_aliases",
                columns: new[] { "alias", "language", "target_level", "target_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_threat_models_code",
                table: "threat_models",
                column: "code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_threat_models_threat_family_id",
                table: "threat_models",
                column: "threat_family_id");

            migrationBuilder.CreateIndex(
                name: "ix_threat_track_observations_observation_id",
                table: "threat_track_observations",
                column: "observation_id");

            migrationBuilder.CreateIndex(
                name: "ix_threat_track_revisions_revision_at",
                table: "threat_track_revisions",
                column: "revision_at")
                .Annotation("Npgsql:IndexMethod", "brin");

            migrationBuilder.CreateIndex(
                name: "ix_threat_track_revisions_threat_track_id_revision_at",
                table: "threat_track_revisions",
                columns: new[] { "threat_track_id", "revision_at" });

            migrationBuilder.CreateIndex(
                name: "ix_threat_tracks_last_location",
                table: "threat_tracks",
                column: "last_location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_threat_tracks_last_location_place_id",
                table: "threat_tracks",
                column: "last_location_place_id");

            migrationBuilder.CreateIndex(
                name: "ix_threat_tracks_status_last_seen_at",
                table: "threat_tracks",
                columns: new[] { "status", "last_seen_at" });

            migrationBuilder.CreateIndex(
                name: "ix_threat_tracks_threat_category_id",
                table: "threat_tracks",
                column: "threat_category_id");

            migrationBuilder.CreateIndex(
                name: "ix_threat_tracks_threat_class_id_status",
                table: "threat_tracks",
                columns: new[] { "threat_class_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ix_threat_tracks_threat_family_id",
                table: "threat_tracks",
                column: "threat_family_id");

            migrationBuilder.CreateIndex(
                name: "ix_threat_tracks_threat_model_id",
                table: "threat_tracks",
                column: "threat_model_id");

            migrationBuilder.CreateIndex(
                name: "ix_user_locations_client_key",
                table: "user_locations",
                column: "client_key",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "air_alerts");

            migrationBuilder.DropTable(
                name: "collector_states");

            migrationBuilder.DropTable(
                name: "processing_errors");

            migrationBuilder.DropTable(
                name: "threat_model_aliases");

            migrationBuilder.DropTable(
                name: "threat_track_observations");

            migrationBuilder.DropTable(
                name: "threat_track_revisions");

            migrationBuilder.DropTable(
                name: "user_locations");

            migrationBuilder.DropTable(
                name: "observations");

            migrationBuilder.DropTable(
                name: "threat_tracks");

            migrationBuilder.DropTable(
                name: "raw_messages");

            migrationBuilder.DropTable(
                name: "places");

            migrationBuilder.DropTable(
                name: "threat_models");

            migrationBuilder.DropTable(
                name: "sources");

            migrationBuilder.DropTable(
                name: "threat_families");

            migrationBuilder.DropTable(
                name: "threat_classes");

            migrationBuilder.DropTable(
                name: "threat_categories");
        }
    }
}
