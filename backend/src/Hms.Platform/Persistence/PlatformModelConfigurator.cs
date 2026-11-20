using Hms.Platform.Outbox;
using Microsoft.EntityFrameworkCore;

namespace Hms.Platform.Persistence;

/// <summary>Технические таблицы схемы <c>platform</c>.</summary>
internal sealed class PlatformModelConfigurator : IModelConfigurator
{
    public const string Schema = "platform";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(e =>
        {
            e.ToTable("outbox_messages", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Type).HasMaxLength(100).IsRequired();
            e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.DedupKey).HasMaxLength(200);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LastError).HasMaxLength(1000);
            e.HasIndex(x => x.DedupKey).IsUnique().HasFilter("dedup_key IS NOT NULL");
            e.HasIndex(x => new { x.Status, x.NextAttemptAt });
        });
    }
}
