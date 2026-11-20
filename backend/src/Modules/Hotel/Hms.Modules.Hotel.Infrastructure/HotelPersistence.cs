using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Hotel.Domain;
using Hms.Platform.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Hms.Modules.Hotel.Infrastructure;

internal sealed class HotelModelConfigurator : IModelConfigurator
{
    public const string Schema = "hotel";

    public void Configure(ModelBuilder b)
    {
        b.Entity<HotelInfo>(e =>
        {
            e.ToTable("hotels", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Address).HasMaxLength(300);
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.TimeZone).HasMaxLength(64).IsRequired();
            e.Property(x => x.Currency).HasMaxLength(3).IsFixedLength().IsRequired();
            e.Property(x => x.Version).IsRowVersion();
        });

        b.Entity<HotelSettings>(e =>
        {
            e.ToTable("hotel_settings", Schema);
            e.HasKey(x => x.HotelId);
            e.HasOne<HotelInfo>().WithOne().HasForeignKey<HotelSettings>(x => x.HotelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Rules).HasMaxLength(5000);
            e.Property(x => x.Locale).HasMaxLength(10).IsRequired();
            e.OwnsOne(x => x.CancellationPolicy, p => p.ToJson("cancellation_policy"));
            e.Property(x => x.Version).IsRowVersion();
            e.ToTable(t =>
            {
                t.HasCheckConstraint("ck_hotel_settings_prepayment", "prepayment_percent BETWEEN 0 AND 100");
                t.HasCheckConstraint("ck_hotel_settings_holds", "payment_hold_minutes > 0 AND pending_hold_hours > 0");
            });
        });

        b.Entity<IntegrationSetting>(e =>
        {
            e.ToTable("integration_settings", Schema);
            e.HasKey(x => x.Id);
            e.HasOne<HotelInfo>().WithMany().HasForeignKey(x => x.HotelId).OnDelete(DeleteBehavior.Cascade);
            e.Property(x => x.Provider).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Mode).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PublicConfig).HasColumnType("jsonb");
            e.Property(x => x.LastCheckStatus).HasMaxLength(200);
            e.HasIndex(x => new { x.HotelId, x.Provider }).IsUnique();
        });
    }
}

/// <summary>Контекст единственной гостиницы с кэшем; сбрасывается при изменении настроек.</summary>
internal sealed class HotelContext(DbContext db, IMemoryCache cache) : IHotelContext
{
    private const string CacheKey = "hotel:settings";

    public async Task<Guid> GetHotelIdAsync(CancellationToken ct) => (await GetSettingsAsync(ct)).HotelId;

    public async Task<HotelSettingsSnapshot> GetSettingsAsync(CancellationToken ct)
    {
        if (cache.TryGetValue(CacheKey, out HotelSettingsSnapshot? cached) && cached is not null)
        {
            return cached;
        }

        var row = await (
            from h in db.Set<HotelInfo>().AsNoTracking()
            join s in db.Set<HotelSettings>().AsNoTracking() on h.Id equals s.HotelId
            orderby h.CreatedAt
            select new { Hotel = h, Settings = s })
            .FirstOrDefaultAsync(ct)
            ?? throw new InvalidOperationException("Гостиница не создана: выполните команду migrate.");

        var s = row.Settings;
        var snapshot = new HotelSettingsSnapshot(
            row.Hotel.Id,
            row.Hotel.Name,
            row.Hotel.TimeZone,
            row.Hotel.Currency,
            s.CheckInTime,
            s.CheckOutTime,
            s.OnlinePaymentEnabled,
            s.PrepaymentPercent,
            s.PaymentHoldMinutes,
            s.PendingHoldHours,
            s.AutoConfirmWebBookings,
            s.AutoReleaseRoomAfterCleaning,
            s.RequireEmailConfirmation,
            s.NoShowCutoffTime,
            new CancellationPolicySnapshot(
                s.CancellationPolicy.FreeCancelHoursBeforeCheckIn,
                s.CancellationPolicy.LateCancelPenaltyNights,
                s.CancellationPolicy.NoShowPenaltyNights,
                s.CancellationPolicy.EarlyDeparturePenaltyNights));

        cache.Set(CacheKey, snapshot, TimeSpan.FromMinutes(5));
        return snapshot;
    }

    public void Invalidate() => cache.Remove(CacheKey);
}

/// <summary>Создаёт гостиницу с настройками по умолчанию и строки интеграций (выключены), если их нет.</summary>
internal sealed partial class HotelSeeder(DbContext db, TimeProvider time, IConfiguration configuration, ILogger<HotelSeeder> logger) : IDataSeeder
{
    public int Order => 5;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var now = time.GetUtcNow();
        var hotel = await db.Set<HotelInfo>().OrderBy(h => h.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (hotel is null)
        {
            // Название пока не выбрано заказчиком — нейтральное, меняется в настройках.
            hotel = new HotelInfo(Guid.CreateVersion7(), configuration["Hotel:Name"] ?? "Гостиница", now);
            db.Set<HotelInfo>().Add(hotel);
            db.Set<HotelSettings>().Add(new HotelSettings(hotel.Id, now));
            LogCreated(logger, hotel.Id);
        }

        var existing = await db.Set<IntegrationSetting>().Where(i => i.HotelId == hotel.Id).Select(i => i.Provider).ToListAsync(cancellationToken);
        foreach (var provider in Enum.GetValues<IntegrationProvider>().Except(existing))
        {
            db.Set<IntegrationSetting>().Add(new IntegrationSetting(hotel.Id, provider, now));
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Создана гостиница {HotelId} с настройками по умолчанию")]
    private static partial void LogCreated(ILogger logger, Guid hotelId);
}
