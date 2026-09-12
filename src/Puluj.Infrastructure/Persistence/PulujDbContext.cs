using Microsoft.EntityFrameworkCore;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence;

public class PulujDbContext(DbContextOptions<PulujDbContext> options) : DbContext(options)
{
    public DbSet<Source> Sources => Set<Source>();
    public DbSet<RawMessage> RawMessages => Set<RawMessage>();

    public DbSet<ThreatCategory> ThreatCategories => Set<ThreatCategory>();
    public DbSet<ThreatClass> ThreatClasses => Set<ThreatClass>();
    public DbSet<ThreatFamily> ThreatFamilies => Set<ThreatFamily>();
    public DbSet<ThreatModel> ThreatModels => Set<ThreatModel>();
    public DbSet<ThreatModelAlias> ThreatModelAliases => Set<ThreatModelAlias>();

    public DbSet<Place> Places => Set<Place>();

    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<ThreatTrack> ThreatTracks => Set<ThreatTrack>();
    public DbSet<ThreatTrackObservation> ThreatTrackObservations => Set<ThreatTrackObservation>();
    public DbSet<ThreatTrackRevision> ThreatTrackRevisions => Set<ThreatTrackRevision>();

    public DbSet<AirAlert> AirAlerts => Set<AirAlert>();
    public DbSet<CollectorState> CollectorStates => Set<CollectorState>();
    public DbSet<ProcessingError> ProcessingErrors => Set<ProcessingError>();
    public DbSet<UserLocation> UserLocations => Set<UserLocation>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(PulujDbContext).Assembly);
    }
}
