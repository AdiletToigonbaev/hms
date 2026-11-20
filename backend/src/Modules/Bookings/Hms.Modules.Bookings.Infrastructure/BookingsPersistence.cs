using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Таблицы схемы <c>bookings</c>. Ограничение против пересечений
/// <c>EXCLUDE USING gist (room_id WITH =, stay_range WITH &amp;&amp;) WHERE (is_blocking)</c>
/// и вычисляемый столбец <c>stay_range daterange [check_in, check_out)</c> создаются SQL в миграции.
/// </summary>
internal sealed class BookingsModelConfigurator : IModelConfigurator
{
    public const string Schema = "bookings";
    public const string NumberSequence = "booking_number_seq";

    public void Configure(ModelBuilder b)
    {
        b.HasSequence<long>(NumberSequence, Schema).StartsAt(1);

        b.Entity<Booking>(e =>
        {
            e.ToTable("bookings", Schema, t =>
            {
                t.HasCheckConstraint("ck_bookings_dates", "check_out > check_in");
                t.HasCheckConstraint("ck_bookings_adults", "adults >= 1");
                t.HasCheckConstraint("ck_bookings_amounts", "total_amount >= 0 AND discount_amount >= 0 AND discount_amount <= accommodation_amount");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(20).IsRequired();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
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
            e.Ignore(x => x.Stay);
            e.Ignore(x => x.Nights);
            e.HasMany(x => x.Rooms).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.Services).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.History).WithOne().HasForeignKey(x => x.BookingId).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Rooms).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.Services).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasIndex(x => new { x.HotelId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.HotelId, x.Status, x.CheckIn });
            e.HasIndex(x => x.GuestId);
        });

        b.Entity<BookingRoom>(e =>
        {
            e.ToTable("booking_rooms", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.HasIndex(x => x.BookingId);
            e.HasIndex(x => new { x.RoomId, x.CheckIn });
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
internal sealed class RoomBookingQueries(DbContext db) : IRoomBookingQueries
{
    public async Task<IReadOnlySet<Guid>> RoomsWithArrivalOnAsync(DateOnly date, CancellationToken ct) =>
        (await db.Set<BookingRoom>().AsNoTracking()
            .Where(r => r.IsBlocking && r.CheckIn == date)
            .Select(r => r.RoomId)
            .Distinct()
            .ToListAsync(ct))
        .ToHashSet();

    public Task<bool> HasAnyBookingAsync(Guid roomId, CancellationToken ct) =>
        db.Set<BookingRoom>().AnyAsync(r => r.RoomId == roomId, ct);

    public Task<bool> HasUpcomingBookingsAsync(Guid roomId, DateOnly today, CancellationToken ct) =>
        db.Set<BookingRoom>().AnyAsync(r => r.RoomId == roomId && r.IsBlocking && r.CheckOut > today, ct);
}
