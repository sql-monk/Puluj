using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Puluj.Infrastructure.Persistence.Migrations;

/// <summary>
/// Two service roles next to the owner (`puluj`, used by the Worker which also migrates):
/// `puluj_reader` for the public map Api (SELECT on everything, EXECUTE on the puluj_* functions) and
/// `puluj_admin` for the admin panel (read-write on tables and sequences, no DDL). Default privileges make the
/// grants stick to tables and functions created by later migrations. The initial passwords equal the role names;
/// change them with ALTER ROLE and put the new ones into the connection strings of the two services.
/// </summary>
[DbContext(typeof(PulujDbContext))]
[Migration("20260913120000_AddDbRoles")]
public partial class AddDbRoles : Migration
{
    private const string UpSql = """
        DO $$
        BEGIN
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'puluj_reader') THEN
                CREATE ROLE puluj_reader LOGIN PASSWORD 'puluj_reader';
            END IF;
            IF NOT EXISTS (SELECT 1 FROM pg_roles WHERE rolname = 'puluj_admin') THEN
                CREATE ROLE puluj_admin LOGIN PASSWORD 'puluj_admin';
            END IF;
            EXECUTE format('GRANT CONNECT ON DATABASE %I TO puluj_reader, puluj_admin', current_database());
        END $$;

        GRANT USAGE ON SCHEMA public TO puluj_reader, puluj_admin;

        GRANT SELECT ON ALL TABLES IN SCHEMA public TO puluj_reader;
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO puluj_reader;

        GRANT SELECT, INSERT, UPDATE, DELETE ON ALL TABLES IN SCHEMA public TO puluj_admin;
        GRANT USAGE, SELECT, UPDATE ON ALL SEQUENCES IN SCHEMA public TO puluj_admin;
        GRANT EXECUTE ON ALL FUNCTIONS IN SCHEMA public TO puluj_admin;

        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT ON TABLES TO puluj_reader;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT EXECUTE ON FUNCTIONS TO puluj_reader;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO puluj_admin;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT USAGE, SELECT, UPDATE ON SEQUENCES TO puluj_admin;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public GRANT EXECUTE ON FUNCTIONS TO puluj_admin;
        """;

    private const string DownSql = """
        ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON TABLES FROM puluj_reader, puluj_admin;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON SEQUENCES FROM puluj_admin;
        ALTER DEFAULT PRIVILEGES IN SCHEMA public REVOKE ALL ON FUNCTIONS FROM puluj_reader, puluj_admin;
        REVOKE ALL ON ALL TABLES IN SCHEMA public FROM puluj_reader, puluj_admin;
        REVOKE ALL ON ALL SEQUENCES IN SCHEMA public FROM puluj_admin;
        REVOKE ALL ON ALL FUNCTIONS IN SCHEMA public FROM puluj_reader, puluj_admin;
        REVOKE USAGE ON SCHEMA public FROM puluj_reader, puluj_admin;
        DO $$
        BEGIN
            EXECUTE format('REVOKE CONNECT ON DATABASE %I FROM puluj_reader, puluj_admin', current_database());
        END $$;
        DROP ROLE IF EXISTS puluj_reader;
        DROP ROLE IF EXISTS puluj_admin;
        """;

    protected override void Up(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(UpSql);

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.Sql(DownSql);
}
