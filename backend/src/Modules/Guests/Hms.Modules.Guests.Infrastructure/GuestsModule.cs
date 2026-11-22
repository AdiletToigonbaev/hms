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
internal sealed class CurrentGuest(DbContext db, IHotelContext hotel, ICurrentUser currentUser) : ICurrentGuest
{
    public async Task<GuestRef?> GetAsync(CancellationToken ct)
    {
        if (currentUser.UserId is not { } userId)
        {
            return null;
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var g = await db.Set<Guest>().AsNoTracking().FirstOrDefaultAsync(x => x.UserId == userId && x.HotelId == hotelId && x.AnonymizedAt == null, ct);
        return g is null ? null : new GuestRef(g.Id, g.FullName, g.Phone, g.Email, false);
    }
}

internal sealed class GuestDirectory(DbContext db, IHotelContext hotel, TimeProvider time) : IGuestDirectory
{
    public async Task<GuestRef?> GetAsync(Guid guestId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var g = await db.Set<Guest>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == guestId && x.HotelId == hotelId, ct);
        return g is null ? null : new GuestRef(g.Id, g.FullName, g.Phone, g.Email, g.IsAnonymized);
    }

    public async Task<IReadOnlyDictionary<Guid, GuestRef>> GetManyAsync(IReadOnlyCollection<Guid> guestIds, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Guest>().AsNoTracking().Where(x => x.HotelId == hotelId && guestIds.Contains(x.Id))
            .ToDictionaryAsync(g => g.Id, g => new GuestRef(g.Id, g.FullName, g.Phone, g.Email, g.AnonymizedAt != null), ct);
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
        services.AddScoped<ICurrentGuest, CurrentGuest>();
        services.AddScoped<IGuestAccount, GuestAccount>();
        services.AddScoped<IGuestRegistration, GuestRegistration>();
        services.AddScoped<IGuestService, GuestService>();
        services.AddValidatorsFromAssemblyContaining<SaveGuestRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapGuestsEndpoints(this IEndpointRouteBuilder api)
    {
        // Согласия в кабинете гостя: обработка ПД — условие аккаунта (отзыв = удаление аккаунта), WhatsApp — по желанию.
        var me = api.MapGroup("/me/consents").WithTags("Account").RequireAuthorization();
        me.MapGet("/", async (IGuestAccount s, ICurrentUser user, CancellationToken ct) =>
            user.UserId is { } id && await s.GetConsentsAsync(id, ct) is { } c ? Results.Ok(c) : GuestErrors.NotFound.ToProblem());
        me.MapPut("/", async (SetMyConsentsRequest r, IGuestAccount s, ICurrentUser user, CancellationToken ct) =>
            user.UserId is { } id ? (await s.SetWhatsAppConsentAsync(id, r.WhatsApp, ct)).ToHttpResult() : GuestErrors.NotFound.ToProblem());

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
