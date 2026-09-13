using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class TargetCategoryConfiguration : IEntityTypeConfiguration<TargetCategory>
{
    public void Configure(EntityTypeBuilder<TargetCategory> b)
    {
        b.HasKey(x => x.TargetCategoryId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);
    }
}

public class TargetClassConfiguration : IEntityTypeConfiguration<TargetClass>
{
    public void Configure(EntityTypeBuilder<TargetClass> b)
    {
        b.HasKey(x => x.TargetClassId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.HasOne(x => x.Category).WithMany(x => x.Classes).HasForeignKey(x => x.TargetCategoryId);
    }
}

public class TargetFamilyConfiguration : IEntityTypeConfiguration<TargetFamily>
{
    public void Configure(EntityTypeBuilder<TargetFamily> b)
    {
        b.HasKey(x => x.TargetFamilyId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.Name).HasMaxLength(128);
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.HasOne(x => x.Class).WithMany(x => x.Families).HasForeignKey(x => x.TargetClassId);
    }
}

public class TargetModelConfiguration : IEntityTypeConfiguration<TargetModel>
{
    public void Configure(EntityTypeBuilder<TargetModel> b)
    {
        b.HasKey(x => x.TargetModelId);
        b.Property(x => x.Code).HasMaxLength(64);
        b.HasIndex(x => x.Code).IsUnique();
        b.Property(x => x.CanonicalName).HasMaxLength(128);
        b.Property(x => x.Manufacturer).HasMaxLength(128);
        b.Property(x => x.Country).HasMaxLength(8);
        b.Property(x => x.Metadata).HasColumnType("jsonb");
        b.HasOne(x => x.Family).WithMany(x => x.Models).HasForeignKey(x => x.TargetFamilyId);
    }
}

public class TargetModelAliasConfiguration : IEntityTypeConfiguration<TargetModelAlias>
{
    public void Configure(EntityTypeBuilder<TargetModelAlias> b)
    {
        b.HasKey(x => x.TargetModelAliasId);
        b.Property(x => x.Alias).HasMaxLength(128);
        b.Property(x => x.Language).HasMaxLength(8);
        b.HasIndex(x => new { x.Alias, x.Language, x.TargetLevel, x.TargetId }).IsUnique();
    }
}
