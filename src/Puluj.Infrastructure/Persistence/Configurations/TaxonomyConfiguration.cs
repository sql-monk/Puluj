using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class ThreatCategoryConfiguration : IEntityTypeConfiguration<ThreatCategory>
{
    public void Configure(EntityTypeBuilder<ThreatCategory> b)
    {
        b.HasKey(x => x.ThreatCategoryId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);
    }
}

public class ThreatClassConfiguration : IEntityTypeConfiguration<ThreatClass>
{
    public void Configure(EntityTypeBuilder<ThreatClass> b)
    {
        b.HasKey(x => x.ThreatClassId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.HasOne(x => x.Category).WithMany(x => x.Classes).HasForeignKey(x => x.ThreatCategoryId);
    }
}

public class ThreatFamilyConfiguration : IEntityTypeConfiguration<ThreatFamily>
{
    public void Configure(EntityTypeBuilder<ThreatFamily> b)
    {
        b.HasKey(x => x.ThreatFamilyId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.HasOne(x => x.Class).WithMany(x => x.Families).HasForeignKey(x => x.ThreatClassId);
    }
}

public class ThreatModelConfiguration : IEntityTypeConfiguration<ThreatModel>
{
    public void Configure(EntityTypeBuilder<ThreatModel> b)
    {
        b.HasKey(x => x.ThreatModelId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.CanonicalName).HasMaxLength(128);
        b.Property(x => x.Manufacturer).HasMaxLength(128);
        b.Property(x => x.Country).HasMaxLength(8);
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.HasOne(x => x.Family).WithMany(x => x.Models).HasForeignKey(x => x.ThreatFamilyId);
    }
}

public class ThreatModelAliasConfiguration : IEntityTypeConfiguration<ThreatModelAlias>
{
    public void Configure(EntityTypeBuilder<ThreatModelAlias> b)
    {
        b.HasKey(x => x.ThreatModelAliasId);
        b.Property(x => x.Alias).HasMaxLength(128);
        b.Property(x => x.Language).HasMaxLength(8);
        b.HasIndex(x => new { x.Alias, x.Language, x.TargetLevel, x.TargetId }).IsUnique();
    }
}
