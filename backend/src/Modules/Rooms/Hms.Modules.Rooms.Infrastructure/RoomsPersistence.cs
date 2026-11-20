using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Rooms.Domain;
using Hms.Platform.Files;
using Hms.Platform.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Rooms.Infrastructure;

internal sealed class RoomsModelConfigurator : IModelConfigurator
{
    public const string Schema = "rooms";

    public void Configure(ModelBuilder b)
    {
        b.Entity<Amenity>(e =>
        {
            e.ToTable("amenities", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Code).HasMaxLength(50).IsRequired();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Icon).HasMaxLength(50);
            e.HasIndex(x => new { x.HotelId, x.Code }).IsUnique();
        });

        b.Entity<RoomType>(e =>
        {
            e.ToTable("room_types", Schema, t =>
            {
                t.HasCheckConstraint("ck_room_types_capacity", "capacity BETWEEN 1 AND 20");
                t.HasCheckConstraint("ck_room_types_base_price", "base_price > 0");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.BedType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.BasePrice).HasPrecision(14, 2);
            e.Property(x => x.Version).IsRowVersion();
            e.HasMany(x => x.Amenities).WithOne().HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Amenities).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasIndex(x => new { x.HotelId, x.IsArchived, x.SortOrder });
        });

        b.Entity<RoomTypeAmenity>(e =>
        {
            e.ToTable("room_type_amenities", Schema);
            e.HasKey(x => new { x.RoomTypeId, x.AmenityId });
            e.HasOne<Amenity>().WithMany().HasForeignKey(x => x.AmenityId).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Room>(e =>
        {
            e.ToTable("rooms", Schema, t =>
            {
                t.HasCheckConstraint("ck_rooms_price_override", "price_override IS NULL OR price_override > 0");
                t.HasCheckConstraint("ck_rooms_capacity", "capacity IS NULL OR capacity BETWEEN 1 AND 20");
            });
            e.HasKey(x => x.Id);
            e.Property(x => x.Number).HasMaxLength(10).IsRequired();
            e.Property(x => x.Description).HasMaxLength(2000);
            e.Property(x => x.BedType).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.PriceOverride).HasPrecision(14, 2);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<RoomType>().WithMany().HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.HotelId, x.Number }).IsUnique();
            e.HasIndex(x => new { x.RoomTypeId, x.Status });
        });

        b.Entity<RoomStatusChange>(e =>
        {
            e.ToTable("room_status_history", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.FromStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ToStatus).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Reason).HasMaxLength(500);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RoomId, x.ChangedAt });
        });

        b.Entity<RoomPhoto>(e =>
        {
            e.ToTable("room_photos", Schema, t =>
                t.HasCheckConstraint("ck_room_photos_owner", "(room_type_id IS NULL) <> (room_id IS NULL)"));
            e.HasKey(x => x.Id);
            e.HasOne<RoomType>().WithMany().HasForeignKey(x => x.RoomTypeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<Room>().WithMany().HasForeignKey(x => x.RoomId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<StoredFile>().WithMany().HasForeignKey(x => x.FileId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.RoomTypeId, x.SortOrder });
            e.HasIndex(x => new { x.RoomId, x.SortOrder });
        });
    }
}

/// <summary>Справочник удобств по умолчанию (добавляет отсутствующие; изменения владельца не трогает).</summary>
internal sealed class AmenitySeeder(DbContext db, IHotelContext hotel) : IDataSeeder
{
    private static readonly (string Code, string Name, string Icon)[] Defaults =
    [
        ("wifi", "Wi-Fi", "wifi"),
        ("ac", "Кондиционер", "snowflake"),
        ("tv", "Телевизор", "tv"),
        ("fridge", "Холодильник", "refrigerator"),
        ("kettle", "Чайник", "coffee"),
        ("shower", "Душ", "shower-head"),
        ("bath", "Ванна", "bath"),
        ("hairdryer", "Фен", "wind"),
        ("safe", "Сейф", "lock"),
        ("desk", "Рабочий стол", "lamp-desk"),
        ("balcony", "Балкон", "fence"),
        ("mountain_view", "Вид на горы", "mountain"),
    ];

    public int Order => 20;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var hotelId = await hotel.GetHotelIdAsync(cancellationToken);
        var existing = await db.Set<Amenity>().Where(a => a.HotelId == hotelId).Select(a => a.Code).ToListAsync(cancellationToken);
        var order = 0;
        foreach (var (code, name, icon) in Defaults)
        {
            order += 10;
            if (!existing.Contains(code))
            {
                db.Set<Amenity>().Add(new Amenity(hotelId, code, name, icon, order));
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }
}
