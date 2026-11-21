using FluentValidation;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Guests.Application;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Guests.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Guests.Infrastructure;

/// <summary>Таблицы схемы <c>guests</c>.</summary>
internal sealed class GuestsModelConfigurator : IModelConfigurator
{
    public const string Schema = "guests";

    public void Configure(ModelBuilder b)
    {
        b.Entity<Guest>(e =>
        {
            e.ToTable("guests", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.FullName).HasMaxLength(200).IsRequired();
            e.Property(x => x.Phone).HasMaxLength(32);
            e.Property(x => x.WhatsAppPhone).HasMaxLength(32);
            e.Property(x => x.Email).HasMaxLength(256);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.IsAnonymized);
            e.HasMany(x => x.Consents).WithOne().HasForeignKey(x => x.GuestId).OnDelete(DeleteBehavior.Restrict);
            e.Navigation(x => x.Consents).UsePropertyAccessMode(PropertyAccessMode.Field);
            // Поиск дублей и быстрый поиск по контактам. Уникальности в БД нет: дубли (семья с одним телефоном)
            // приложение показывает администратору, а не запрещает молча; слияние — P1.
            e.HasIndex(x => new { x.HotelId, x.Phone });
            e.HasIndex(x => new { x.HotelId, x.Email });
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
            e.HasIndex(x => new { x.HotelId, x.CreatedAt });
        });

        b.Entity<GuestConsent>(e =>
        {
            e.ToTable("guest_consents", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Source).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(x => new { x.GuestId, x.Type, x.RecordedAt });
        });
    }
}

/// <summary>Карточки гостей для модуля Bookings.</summary>
internal sealed class GuestDirectory(DbContext db, IHotelContext hotel, TimeProvider time) : IGuestDirectory
{
    public async Task<GuestRef?> GetAsync(Guid guestId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var g = await db.Set<Guest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == guestId && x.HotelId == hotelId, ct);
        return g is null ? null : new GuestRef(g.Id, g.FullName, g.Phone, g.Email, g.IsAnonymized);
    }

    public async Task<Result<GuestRef>> FindOrCreateAsync(GuestContact contact, CancellationToken ct)
    {
        var phone = string.IsNullOrWhiteSpace(contact.Phone) ? null : PhoneNumber.Parse(contact.Phone);
        if (phone is { IsFailure: true })
        {
            return phone.Error;
        }

        var email = Guest.NormalizeEmail(contact.Email);
        if (phone is null && email is null)
        {
            return GuestErrors.ContactRequired;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = time.GetUtcNow();
        var existing = await GuestLookup.FindByContactAsync(db, hotelId, phone?.Value.Value, email, ct);
        if (existing is not null)
        {
            existing.FillMissing(phone?.Value, email, now);
            return new GuestRef(existing.Id, existing.FullName, existing.Phone, existing.Email, false);
        }

        var guest = Guest.Create(hotelId, GuestSource.Booking, contact.FullName, phone?.Value, email, now);
        db.Set<Guest>().Add(guest);
        return new GuestRef(guest.Id, guest.FullName, guest.Phone, guest.Email, false);
    }
}

/// <summary>Регистрация на сайте: существующая карточка (по email или телефону) привязывается к аккаунту, иначе создаётся новая.</summary>
internal sealed class GuestRegistration(DbContext db, IHotelContext hotel, TimeProvider time) : IGuestRegistration
{
    public async Task RegisterPortalGuestAsync(Guid userId, GuestContact contact, bool personalDataConsent, bool whatsAppConsent, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var now = time.GetUtcNow();
        PhoneNumber? phone = PhoneNumber.Parse(contact.Phone) is { IsSuccess: true } p ? p.Value : null;
        var email = Guest.NormalizeEmail(contact.Email);

        var guest = await GuestLookup.FindByContactAsync(db, hotelId, phone?.Value, email, ct, includeConsents: true);
        if (guest is null || guest.UserId is not null)
        {
            guest = Guest.Create(hotelId, GuestSource.Portal, contact.FullName, phone, email, now);
            db.Set<Guest>().Add(guest);
        }
        else
        {
            guest.FillMissing(phone, email, now);
        }

        guest.LinkUser(userId, now);
        if (personalDataConsent)
        {
            guest.RecordConsent(ConsentType.PersonalData, true, ConsentSource.Portal, userId, now);
        }

        if (whatsAppConsent)
        {
            guest.RecordConsent(ConsentType.WhatsApp, true, ConsentSource.Portal, userId, now);
        }
    }
}

internal static class GuestLookup
{
    /// <summary>Действующая карточка по телефону, иначе по email (самая ранняя — основная).</summary>
    public static async Task<Guest?> FindByContactAsync(DbContext db, Guid hotelId, string? phone, string? email, CancellationToken ct, bool includeConsents = false)
    {
        IQueryable<Guest> guests = db.Set<Guest>().Where(g => g.HotelId == hotelId && g.AnonymizedAt == null);
        if (includeConsents)
        {
            guests = guests.Include(g => g.Consents);
        }

        if (phone is not null && await guests.OrderBy(g => g.CreatedAt).FirstOrDefaultAsync(g => g.Phone == phone, ct) is { } byPhone)
        {
            return byPhone;
        }

        return email is null ? null : await guests.OrderBy(g => g.CreatedAt).FirstOrDefaultAsync(g => g.Email == email, ct);
    }
}

public static class GuestsModule
{
    public static IServiceCollection AddGuestsModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, GuestsModelConfigurator>();
        services.AddScoped<IGuestDirectory, GuestDirectory>();
        services.AddScoped<IGuestRegistration, GuestRegistration>();
        services.AddScoped<IGuestService, GuestService>();
        services.AddValidatorsFromAssemblyContaining<SaveGuestRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapGuestsEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/guests").WithTags("Guests");
        g.MapGet("/", async ([AsParameters] GuestQuery q, IGuestService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequirePermission(Permissions.GuestView);
        // Выбор гостя при брони — нужен и тем, кто только создаёт брони.
        g.MapGet("/lookup", async (string? q, IGuestService s, CancellationToken ct) => Results.Ok(await s.LookupAsync(q, ct)))
            .RequireAnyPermission(Permissions.GuestView, Permissions.BookingCreate);
        g.MapPost("/", async (SaveGuestRequest r, IGuestService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(x => Results.Created($"/api/v1/guests/{x.Id}", x)))
            .WithValidation<SaveGuestRequest>().RequirePermission(Permissions.GuestManage);
        g.MapGet("/{id:guid}", async (Guid id, IGuestService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.GuestView);
        g.MapPut("/{id:guid}", async (Guid id, SaveGuestRequest r, IGuestService s, CancellationToken ct) => (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveGuestRequest>().RequirePermission(Permissions.GuestManage);
        g.MapGet("/{id:guid}/bookings", async (Guid id, IGuestService s, CancellationToken ct) => (await s.GetBookingsAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.GuestView).RequirePermission(Permissions.BookingView);
        g.MapGet("/{id:guid}/consents", async (Guid id, IGuestService s, CancellationToken ct) => (await s.GetConsentHistoryAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.GuestView);
        g.MapPost("/{id:guid}/consents", async (Guid id, SetConsentRequest r, IGuestService s, CancellationToken ct) => (await s.SetConsentAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SetConsentRequest>().RequirePermission(Permissions.GuestManage);
        g.MapPost("/{id:guid}/anonymize", async (Guid id, IGuestService s, CancellationToken ct) => (await s.AnonymizeAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.GuestAnonymize);
        return api;
    }
}
