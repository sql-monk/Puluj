using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class SourceConfiguration : IEntityTypeConfiguration<Source>
{
    public void Configure(EntityTypeBuilder<Source> b)
    {
        b.HasKey(x => x.SourceId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(256);
        b.Property(x => x.Url).HasMaxLength(1024);
        b.Property(x => x.Config).HasColumnType("jsonb");
        b.HasOne(x => x.CollectorState).WithOne(x => x.Source).HasForeignKey<CollectorState>(x => x.SourceId);
    }
}

public class CollectorStateConfiguration : IEntityTypeConfiguration<CollectorState>
{
    public void Configure(EntityTypeBuilder<CollectorState> b)
    {
        b.HasKey(x => x.SourceId);
        b.Property(x => x.LastSourceMessageId).HasMaxLength(256);
        b.Property(x => x.Cursor).HasColumnType("jsonb");
    }
}

public class ProcessingErrorConfiguration : IEntityTypeConfiguration<ProcessingError>
{
    public void Configure(EntityTypeBuilder<ProcessingError> b)
    {
        b.HasKey(x => x.ProcessingErrorId);
        b.Property(x => x.Stage).HasMaxLength(64);
        b.Property(x => x.Payload).HasColumnType("jsonb");
        b.HasIndex(x => x.OccurredAt);
        b.HasIndex(x => x.RawMessageId);
    }
}

public class UserLocationConfiguration : IEntityTypeConfiguration<UserLocation>
{
    public void Configure(EntityTypeBuilder<UserLocation> b)
    {
        b.HasKey(x => x.UserLocationId);
        b.Property(x => x.ClientKey).HasMaxLength(128);
        b.HasIndex(x => x.ClientKey).IsUnique();
        b.Property(x => x.Location).HasColumnType("geography (point, 4326)");
    }
}

public class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> b)
    {
        b.HasKey(x => x.Key);
        b.Property(x => x.Key).HasMaxLength(128);
    }
}
