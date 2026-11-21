using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Platform.Persistence;
using Hms.Platform.Time;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Таблицы схемы <c>bookings</c>. Ограничение против пересечений
/// <c>EXCLUDE USING gist (room_id WITH =, occupancy WITH &amp;&amp;) WHERE (is_blocking)</c>
/// и вычисляемый столбец <c>occupancy tstzrange [starts_at, blocked_until)</c> создаются SQL в миграции.
/// </summary>
internal sealed class BookingsModelConfigurator : IModelConfigurator
{
    public const string Schema = "bookings";

    public void Configure(ModelBuilder b)
    {
        b.Entity<BookingNumberCounter>(e =>
        {
            e.ToTable("booking_number_counters", Schema);
            e.HasKey(x => new { x.HotelId, x.Day });
        });

        b.Entity<Booking>(e =>
        {
            e.ToTable("bookings", Schema, t =>
            {
                t.HasCheckConstraint("ck_bookings_dates", "check_out >= check_in AND ends_at > starts_at");
                t.HasCheckConstraint("ck_bookings_hours", "hours IS NULL OR hours BETWEEN 1 AND 24");
                t.HasCheckConstraint("ck_bookings_adults", "adults >= 1");
                t.HasCheckConstraint("ck_bookings_amounts", "total_amount >= 0 AND discount_amount >= 0 AND discount_amount <= accommodation_amount");
                t.HasCheckConstraint("ck_bookings_penalty", "penalty_amount >= 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Kind).HasConversion<string>().HasMaxLength(10).HasDefaultValue(BookingKind.Nightly).HasSentinel(BookingKind.Nightly);
            e.Property(x => x.ExtensionsAmount).HasPrecision(14, 2);
            e.Property(x => x.GuestName).HasMaxLength(200).IsRequired();
            e.Property(x => x.GuestPhone).HasMaxLength(32);
            e.Property(x => x.GuestEmail).HasMaxLength(256);
            e.Property(x => x.Comment).HasMaxLength(1000);
            e.Property(x => x.PromoCode).HasMaxLength(30);
            e.Property(x => x.CancelReason).HasMaxLength(500);
            e.Property(x => x.AccommodationAmount).HasPrecision(14, 2);
            e.Property(x => x.DiscountAmount).HasPrecision(14, 2);
            e.Property(x => x.ServicesAmount).HasPrecision(14, 2);
            e.Property(x => x.TotalAmount).HasPrecision(14, 2);
            e.Property(x => x.PriceSnapshot).HasColumnType("jsonb").IsRequired();
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.Nights);
            e.Ignore(x => x.Units);
            e.Ignore(x => x.ActiveExtensions);
            e.Property(x => x.PenaltyAmount).HasPrecision(14, 2);
            e.Property(x => x.PenaltyKind).HasConversion<string>().HasMaxLength(20);
            e.HasMany(x => x.Rooms).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Services).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Extensions).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Extensions).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Rooms).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
            // Номер «Ч-2111-07» повторяется через год — уникален вместе с годом создания.
            e.HasIndex(x => new { x.HotelId, x.NumberYear, x.Number }).IsUnique();
            e.HasIndex(x => new { x.HotelId, x.Status, x.StartsAt });
            e.HasIndex(x => x.GuestId);
            // Фоновая задача ищет неподтверждённые брони с истёкшим удержанием.
            e.HasIndex(x => x.HoldUntil).HasFilter("hold_until IS NOT NULL");
        });

        b.Entity<BookingRoom>(e =>
        {
            e.ToTable("booking_rooms", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => new { x.RoomId, x.CheckIn });
            e.HasIndex(x => new { x.RoomId, x.StartsAt });
        });

        b.Entity<BookingExtension>(e =>
        {
            e.ToTable("booking_extensions", Schema, t => t.HasCheckConstraint("ck_booking_extensions_qty", "quantity > 0 AND amount >= 0 AND new_ends_at > previous_ends_at"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Unit).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Amount).HasPrecision(14, 2);
            e.Property(x => x.PriceSnapshot).HasColumnType("jsonb").IsRequired();
            e.HasIndex(x => x.BookingId);
        });

        b.Entity<BookingServiceLine>(e =>
        {
            e.ToTable("booking_services", Schema, t => t.HasCheckConstraint("ck_booking_services_qty", "quantity > 0"));
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.UnitPrice).HasPrecision(14, 2);
            e.Property(x => x.Total).HasPrecision(14, 2);
        });

        b.Entity<BookingHistoryEntry>(e =>
        {
            e.ToTable("booking_history", Schema);
            e.HasKey(x => x.Id);
            // Id задаёт домен: новая запись в коллекции загруженной брони должна вставляться, а не обновляться.
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EventType).HasMaxLength(50).IsRequired();
            e.Property(x => x.OldValues).HasColumnType("jsonb");
            e.Property(x => x.NewValues).HasColumnType("jsonb");
            e.Property(x => x.Comment).HasMaxLength(500);
            e.HasIndex(x => new { x.BookingId, x.ChangedAt });
        });
    }
}

/// <summary>Запросы модуля Rooms к бронированиям.</summary>
internal sealed class RoomBookingQueries(DbContext db, IHotelClock clock) : IRoomBookingQueries
{
    public async Task<IReadOnlyDictionary<Guid, RoomTodayState>> TodayStatesAsync(DateTimeOffset now, DateTimeOffset dayEnd, CancellationToken ct)
    {
        var rows = await db.Set<BookingRoom>().AsNoTracking()
            .Where(r => r.IsBlocking && r.EndsAt > now && r.StartsAt < dayEnd)
            .Select(r => new { r.RoomId, r.StartsAt, r.EndsAt })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.RoomId).ToDictionary(
            g => g.Key,
            g => new RoomTodayState(
                g.Where(r => r.StartsAt <= now).Select(r => (DateTimeOffset?)r.EndsAt).Max(),
                g.Where(r => r.StartsAt > now).Select(r => (DateTimeOffset?)r.StartsAt).Min()));
    }

    public Task<bool> HasAnyBookingAsync(Guid roomId, CancellationToken ct) =>
        db.Set<BookingRoom>().AnyAsync(r => r.RoomId == roomId, ct);

    /// <summary>Действующие брони, которые ещё не закончились (с учётом уборки).</summary>
    public Task<bool> HasUpcomingBookingsAsync(Guid roomId, DateOnly today, CancellationToken ct)
    {
        var now = clock.UtcNow;
        return db.Set<BookingRoom>().AnyAsync(r => r.RoomId == roomId && r.IsBlocking && r.BlockedUntil > now, ct);
    }
}

/// <summary>Начисления по броням для финансов.</summary>
internal sealed class BookingCharges(DbContext db, Hms.Modules.Hotel.Contracts.IHotelContext hotel, Hms.Modules.Rooms.Contracts.IRoomDirectory rooms) : IBookingCharges
{
    public async Task<BookingCharge?> GetAsync(Guid bookingId, CancellationToken ct) =>
        (await ListAsync([bookingId], ct)) is [var first, ..] ? first : null;

    public async Task<IReadOnlyList<BookingCharge>> ListAsync(IReadOnlyCollection<Guid> bookingIds, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await QueryAsync(db.Set<Booking>().Where(b => b.HotelId == hotelId && bookingIds.Contains(b.Id)), ct);
    }

    public async Task<IReadOnlyList<BookingCharge>> ListForGuestAsync(Guid guestId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await QueryAsync(db.Set<Booking>().Where(b => b.HotelId == hotelId && b.GuestId == guestId), ct);
    }

    public async Task<IReadOnlyList<BookingCharge>> ListWithDueAsync(CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await QueryAsync(
            db.Set<Booking>().Where(b => b.HotelId == hotelId
                && (((b.Status == BookingStatus.Cancelled || b.Status == BookingStatus.NoShow) && b.PenaltyAmount > 0)
                    || (b.Status != BookingStatus.Cancelled && b.Status != BookingStatus.NoShow && b.Status != BookingStatus.Expired && b.TotalAmount > 0))),
            ct);
    }

    private async Task<IReadOnlyList<BookingCharge>> QueryAsync(IQueryable<Booking> query, CancellationToken ct)
    {
        var rows = await query.AsNoTracking()
            .Select(b => new
            {
                b.Id,
                b.Number,
                b.Status,
                b.Kind,
                b.GuestId,
                b.GuestName,
                b.StartsAt,
                b.EndsAt,
                b.TotalAmount,
                b.PenaltyAmount,
                RoomId = b.Rooms.Select(r => r.RoomId).FirstOrDefault(),
            })
            .ToListAsync(ct);
        var roomInfo = await rooms.GetManyAsync(rows.Select(r => r.RoomId).Distinct(), ct);
        return rows.Select(r => new BookingCharge(r.Id, r.Number, r.Status, r.Kind.ToString(), r.GuestId, r.GuestName,
                roomInfo.TryGetValue(r.RoomId, out var room) ? room.Number : null, r.StartsAt, r.EndsAt, r.TotalAmount, r.PenaltyAmount))
            .ToList();
    }
}
