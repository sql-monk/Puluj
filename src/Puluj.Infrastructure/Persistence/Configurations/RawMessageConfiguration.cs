using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Puluj.Domain.Entities;

namespace Puluj.Infrastructure.Persistence.Configurations;

public class RawMessageConfiguration : IEntityTypeConfiguration<RawMessage>
{
    public void Configure(EntityTypeBuilder<RawMessage> b)
    {
        b.HasKey(x => x.RawMessageId);
        b.Property(x => x.SourceMessageId).HasMaxLength(256);
        b.Property(x => x.Hash).HasMaxLength(64);
        b.Property(x => x.Url).HasMaxLength(2048);
        b.Property(x => x.RawPayload).HasColumnType("jsonb");
        b.Property(x => x.ClaimedBy).HasMaxLength(64);

        // Idempotency keys (spec §5)
        b.HasIndex(x => new { x.SourceId, x.SourceMessageId }).IsUnique();
        b.HasIndex(x => x.Hash).IsUnique();

        b.HasIndex(x => x.ProcessingStatus).HasFilter("processing_status = 0");
        // The sweep takes pending messages oldest-published first; the watchdog asks for the oldest pending one.
        b.HasIndex(x => x.PublishedAt, "ix_raw_messages_pending_published").HasDatabaseName("ix_raw_messages_pending_published").HasFilter("processing_status = 0");
        // Expired claims (processor crashed mid-message) are found by claimed_at; only the few InProgress rows are indexed.
        b.HasIndex(x => x.ClaimedAt, "ix_raw_messages_in_progress_claimed_at").HasDatabaseName("ix_raw_messages_in_progress_claimed_at").HasFilter("processing_status = 4");
        b.HasIndex(x => x.ReceivedAt).HasMethod("brin");
        b.HasIndex(x => x.PublishedAt).HasMethod("brin");

        b.HasOne(x => x.Source).WithMany().HasForeignKey(x => x.SourceId).OnDelete(DeleteBehavior.Restrict);
    }
}
