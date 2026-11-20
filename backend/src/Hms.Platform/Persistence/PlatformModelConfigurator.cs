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

        modelBuilder.Entity<Files.StoredFile>(e =>
        {
            e.ToTable("stored_files", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Path).HasMaxLength(300).IsRequired();
            e.Property(x => x.ContentType).HasMaxLength(100).IsRequired();
            e.Property(x => x.Sha256).HasMaxLength(64).IsRequired();
            e.Property(x => x.OriginalName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Visibility).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => x.Path).IsUnique();
        });

        modelBuilder.Entity<Http.IdempotencyRecord>(e =>
        {
            e.ToTable("idempotency_keys", Schema);
            e.HasKey(x => new { x.Scope, x.Key });
            e.Property(x => x.Scope).HasMaxLength(200);
            e.Property(x => x.Key).HasMaxLength(100);
            e.Property(x => x.RequestHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.ResponseBody).HasColumnType("jsonb");
            e.HasIndex(x => x.ExpiresAt);
        });
    }
}
