using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class TargetLinkConfiguration : IEntityTypeConfiguration<TargetLink>
{
    public void Configure(EntityTypeBuilder<TargetLink> b)
    {
        b.HasKey(x => new { x.FromTargetId, x.ToTargetId });
        b.HasIndex(x => x.ToTargetId);
        b.HasIndex(x => x.CreatedAt);
        b.HasOne(x => x.From).WithMany().HasForeignKey(x => x.FromTargetId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.To).WithMany().HasForeignKey(x => x.ToTargetId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class SourceCopyConfiguration : IEntityTypeConfiguration<SourceCopy>
{
    public void Configure(EntityTypeBuilder<SourceCopy> b)
    {
        b.HasKey(x => new { x.CopierSourceId, x.OriginalSourceId, x.Day });
        b.HasIndex(x => x.Day);
    }
}

public class SourceDailyStatConfiguration : IEntityTypeConfiguration<SourceDailyStat>
{
    public void Configure(EntityTypeBuilder<SourceDailyStat> b)
    {
        b.HasKey(x => new { x.SourceId, x.Day });
        b.HasIndex(x => x.Day);
    }
}
