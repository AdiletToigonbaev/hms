using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Guests.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Messaging.Contracts;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Guests.Infrastructure;

/// <summary>Аккаунт гостя на сайте: профиль → карточка, согласия, удаление аккаунта с обезличиванием (ТЗ §7, §11).</summary>
internal sealed class GuestAccount(DbContext db, IHotelContext hotel, IGuestBookingQueries bookings, IGuestMessagesCleanup messages, IAuditTrail audit, TimeProvider time) : IGuestAccount
{
    private static readonly BookingStatus[] Active =
        [BookingStatus.Pending, BookingStatus.AwaitingPayment, BookingStatus.Confirmed, BookingStatus.CheckedIn];

    public async Task SyncProfileAsync(Guid userId, string fullName, string? phone, CancellationToken ct)
    {
        if (await LoadAsync(userId, ct) is not { } guest)
        {
            return;
        }

        PhoneNumber? parsed = PhoneNumber.Parse(phone) is { IsSuccess: true } p ? p.Value : null;
        var changed = guest.Update(fullName, parsed ?? (guest.Phone is null ? null : PhoneNumber.Parse(guest.Phone).Value),
            guest.WhatsAppPhone is null ? null : PhoneNumber.Parse(guest.WhatsAppPhone).Value, guest.Email, guest.BirthDate, guest.Notes, time.GetUtcNow());
        if (changed.Count > 0)
        {
            // В аудит — только названия полей, без самих данных.
            audit.Record(new AuditEntry("guest.updated_by_guest", "Guest", guest.Id.ToString(), After: new { Fields = changed }));
        }
    }

    public async Task<PortalConsents?> GetConsentsAsync(Guid userId, CancellationToken ct) =>
        await LoadAsync(userId, ct) is { } guest ? ToDto(guest) : null;

    public async Task<Result<PortalConsents>> SetWhatsAppConsentAsync(Guid userId, bool granted, CancellationToken ct)
    {
        if (await LoadAsync(userId, ct) is not { } guest)
        {
            return GuestErrors.NotFound;
        }

        if (guest.RecordConsent(ConsentType.WhatsApp, granted, ConsentSource.Portal, userId, time.GetUtcNow()))
        {
            audit.Record(new AuditEntry(granted ? "guest.consent_granted" : "guest.consent_withdrawn", "Guest", guest.Id.ToString(), After: new { Type = "WhatsApp" }));
            await db.SaveChangesAsync(ct);
        }

        return ToDto(guest);
    }

    public async Task<Result> RemoveAsync(Guid userId, CancellationToken ct)
    {
        if (await LoadAsync(userId, ct) is not { } guest)
        {
            return Result.Success();
        }

        if ((await bookings.ListAsync(guest.Id, ct)).Any(b => Active.Contains(b.Status)))
        {
            return Error.BusinessRule("account.active_bookings", "У вас есть действующие брони. Отмените их или дождитесь выезда, затем удалите аккаунт.");
        }

        var result = guest.Anonymize(time.GetUtcNow(), accountRemoved: true);
        if (result.IsFailure)
        {
            return result;
        }

        await bookings.AnonymizeAsync(guest.Id, Guest.AnonymizedName, ct);
        await messages.AnonymizeAsync(guest.Id, ct);
        audit.Record(new AuditEntry("guest.anonymized_by_guest", "Guest", guest.Id.ToString()));
        return Result.Success();
    }

    private async Task<Guest?> LoadAsync(Guid userId, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        return await db.Set<Guest>().Include(g => g.Consents)
            .FirstOrDefaultAsync(g => g.UserId == userId && g.HotelId == hotelId && g.AnonymizedAt == null, ct);
    }

    private static PortalConsents ToDto(Guest g)
    {
        var pd = g.CurrentConsent(ConsentType.PersonalData);
        var wa = g.CurrentConsent(ConsentType.WhatsApp);
        return new PortalConsents(pd?.Granted ?? false, pd?.RecordedAt, wa?.Granted ?? false, wa?.RecordedAt);
    }
}
