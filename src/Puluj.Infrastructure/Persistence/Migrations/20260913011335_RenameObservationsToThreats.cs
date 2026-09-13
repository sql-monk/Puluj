using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Observation became Threat: tables, columns, indexes and constraints are renamed in place (hand-written: the
    /// scaffolded version would have dropped and recreated the tables and lost every row).
    /// </summary>
    public partial class RenameObservationsToThreats : Migration
    {
        private static readonly (string Old, string New)[] ThreatIndexes =
        [
            ("ix_observations_destination_place_id", "ix_threats_destination_place_id"),
            ("ix_observations_duplicate_of_observation_id", "ix_threats_duplicate_of_threat_id"),
            ("ix_observations_location", "ix_threats_location"),
            ("ix_observations_location_place_id", "ix_threats_location_place_id"),
            ("ix_observations_observed_at", "ix_threats_observed_at"),
            ("ix_observations_origin_place_id", "ix_threats_origin_place_id"),
            ("ix_observations_raw_message_id", "ix_threats_raw_message_id"),
            ("ix_observations_source_id", "ix_threats_source_id"),
            ("ix_observations_threat_category_id", "ix_threats_threat_category_id"),
            ("ix_observations_threat_class_id_observed_at", "ix_threats_threat_class_id_observed_at"),
            ("ix_observations_threat_family_id", "ix_threats_threat_family_id"),
            ("ix_observations_threat_model_id", "ix_threats_threat_model_id"),
        ];

        private static readonly (string Table, string Old, string New)[] Constraints =
        [
            ("threats", "pk_observations", "pk_threats"),
            ("threats", "fk_observations_observations_duplicate_of_observation_id", "fk_threats_threats_duplicate_of_threat_id"),
            ("threats", "fk_observations_places_destination_place_id", "fk_threats_places_destination_place_id"),
            ("threats", "fk_observations_places_location_place_id", "fk_threats_places_location_place_id"),
            ("threats", "fk_observations_places_origin_place_id", "fk_threats_places_origin_place_id"),
            ("threats", "fk_observations_raw_messages_raw_message_id", "fk_threats_raw_messages_raw_message_id"),
            ("threats", "fk_observations_sources_source_id", "fk_threats_sources_source_id"),
            ("threats", "fk_observations_threat_categories_threat_category_id", "fk_threats_threat_categories_threat_category_id"),
            ("threats", "fk_observations_threat_classes_threat_class_id", "fk_threats_threat_classes_threat_class_id"),
            ("threats", "fk_observations_threat_families_threat_family_id", "fk_threats_threat_families_threat_family_id"),
            ("threats", "fk_observations_threat_models_threat_model_id", "fk_threats_threat_models_threat_model_id"),
            ("track_threats", "pk_threat_track_observations", "pk_track_threats"),
            ("track_threats", "fk_threat_track_observations_observations_observation_id", "fk_track_threats_threats_threat_id"),
            ("track_threats", "fk_threat_track_observations_threat_tracks_threat_track_id", "fk_track_threats_threat_tracks_threat_track_id"),
        ];

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameTable(name: "observations", newName: "threats");
            migrationBuilder.RenameTable(name: "threat_track_observations", newName: "track_threats");

            migrationBuilder.RenameColumn(name: "observation_id", table: "threats", newName: "threat_id");
            migrationBuilder.RenameColumn(name: "duplicate_of_observation_id", table: "threats", newName: "duplicate_of_threat_id");
            migrationBuilder.RenameColumn(name: "observation_confidence", table: "threats", newName: "confidence");
            migrationBuilder.RenameColumn(name: "observation_id", table: "track_threats", newName: "threat_id");
            migrationBuilder.RenameColumn(name: "observation_count", table: "threat_tracks", newName: "threat_count");
            migrationBuilder.RenameColumn(name: "last_observation_id", table: "threat_tracks", newName: "last_threat_id");
            migrationBuilder.RenameColumn(name: "observation_id", table: "threat_track_revisions", newName: "threat_id");
            migrationBuilder.RenameColumn(name: "observation_count", table: "threat_track_revisions", newName: "threat_count");

            foreach (var (old, @new) in ThreatIndexes)
            {
                migrationBuilder.RenameIndex(name: old, table: "threats", newName: @new);
            }
            migrationBuilder.RenameIndex(name: "ix_threat_track_observations_observation_id", table: "track_threats", newName: "ix_track_threats_threat_id");
            foreach (var (table, old, @new) in Constraints)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} RENAME CONSTRAINT {old} TO {@new};");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            foreach (var (table, old, @new) in Constraints)
            {
                migrationBuilder.Sql($"ALTER TABLE {table} RENAME CONSTRAINT {@new} TO {old};");
            }
            migrationBuilder.RenameIndex(name: "ix_track_threats_threat_id", table: "track_threats", newName: "ix_threat_track_observations_observation_id");
            foreach (var (old, @new) in ThreatIndexes)
            {
                migrationBuilder.RenameIndex(name: @new, table: "threats", newName: old);
            }

            migrationBuilder.RenameColumn(name: "threat_count", table: "threat_track_revisions", newName: "observation_count");
            migrationBuilder.RenameColumn(name: "threat_id", table: "threat_track_revisions", newName: "observation_id");
            migrationBuilder.RenameColumn(name: "last_threat_id", table: "threat_tracks", newName: "last_observation_id");
            migrationBuilder.RenameColumn(name: "threat_count", table: "threat_tracks", newName: "observation_count");
            migrationBuilder.RenameColumn(name: "threat_id", table: "track_threats", newName: "observation_id");
            migrationBuilder.RenameColumn(name: "confidence", table: "threats", newName: "observation_confidence");
            migrationBuilder.RenameColumn(name: "duplicate_of_threat_id", table: "threats", newName: "duplicate_of_observation_id");
            migrationBuilder.RenameColumn(name: "threat_id", table: "threats", newName: "observation_id");

            migrationBuilder.RenameTable(name: "track_threats", newName: "threat_track_observations");
            migrationBuilder.RenameTable(name: "threats", newName: "observations");
        }
    }
}
