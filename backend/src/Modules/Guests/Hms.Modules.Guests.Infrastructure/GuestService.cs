using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Guests.Application;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Guests.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Guests.Infrastructure;

/// <summary>
/// База гостей (ТЗ §7). Персональные данные в журнал аудита не пишутся — только названия изменённых полей.
/// </summary>
internal sealed class GuestService(
    DbContext db,
    IHotelContext hotel,
    IGuestBookingQueries bookings,
    IUserDirectory users,
    ICurrentUser currentUser,
    IAuditTrail audit,
    TimeProvider time) : IGuestService
{
    public async Task<PagedResult<GuestListItemDto>> ListAsync(GuestQuery q, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var page = new PageRequest(q.Page, q.PageSize);
        var query = Search(db.Set<Guest>().AsNoTracking().Where(g => g.HotelId == hotelId && g.AnonymizedAt == null), q.Search);

        switch (q.Filter)
        {
            case "whatsApp":
                query = query.Where(g => g.Consents.Where(c => c.Type == ConsentType.WhatsApp).OrderByDescending(c => c.RecordedAt).Select(c => c.Granted).FirstOrDefault());
                break;
            case "noContact":
                query = query.Where(g => g.Phone == null && g.Email == null);
                break;
            case "anonymized":
                query = Search(db.Set<Guest>().AsNoTracking().Where(g => g.HotelId == hotelId && g.AnonymizedAt != null), q.Search);
                break;
        }

        var withBookings = q.Filter == "withBookings";
        var guests = await query
            .OrderByDescending(g => g.UpdatedAt)
            .Select(g => new
            {
                Guest = g,
                WhatsApp = g.Consents.Where(c => c.Type == ConsentType.WhatsApp).OrderByDescending(c => c.RecordedAt).Select(c => c.Granted).FirstOrDefault(),
            })
            .ToListAsync(ct);

        // Гостей одной гостиницы — сотни или тысячи: цифры по броням считаем одним запросом для отобранных.
        var stats = await bookings.StatsAsync(guests.Select(x => x.Guest.Id).ToList(), ct);
        var rows = guests
            .Select(x => (x.Guest, x.WhatsApp, Stats: stats.GetValueOrDefault(x.Guest.Id)))
            .Where(x => !withBookings || x.Stats?.BookingsCount > 0)
            .ToList();

        var items = rows.Skip(page.Skip).Take(page.PageSize)
            .Select(x => new GuestListItemDto(x.Guest.Id, x.Guest.FullName, x.Guest.Phone, x.Guest.Email, x.Guest.UserId != null, x.WhatsApp,
                x.Guest.IsAnonymized, x.Guest.CreatedAt, ToDto(x.Stats)))
            .ToList();
        return new PagedResult<GuestListItemDto>(items, page.Page, page.PageSize, rows.Count);
    }

    public async Task<IReadOnlyList<GuestLookupDto>> LookupAsync(string? query, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(query) || query.Trim().Length < 2)
        {
            return [];
        }

        var hotelId = await hotel.GetHotelIdAsync(ct);
        var guests = await Search(db.Set<Guest>().AsNoTracking().Where(g => g.HotelId == hotelId && g.AnonymizedAt == null), query)
            .OrderByDescending(g => g.UpdatedAt)
            .Take(8)
            .ToListAsync(ct);
        var stats = await bookings.StatsAsync(guests.Select(g => g.Id).ToList(), ct);
        return guests.Select(g => new GuestLookupDto(g.Id, g.FullName, g.Phone, g.Email, stats.GetValueOrDefault(g.Id)?.BookingsCount ?? 0)).ToList();
    }

    public async Task<Result<GuestDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var guest = await LoadAsync(id, tracking: false, ct);
        return guest is null ? GuestErrors.NotFound : await ToDtoAsync(guest, ct);
    }

    public async Task<Result<GuestDto>> CreateAsync(SaveGuestRequest r, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var (phone, whatsApp, email) = Contacts(r);
        if (await DuplicateAsync(hotelId, null, phone?.Value, email, ct) is { } duplicate)
        {
            return duplicate;
        }

        var now = time.GetUtcNow();
        var guest = Guest.Create(hotelId, GuestSource.Admin, r.FullName, phone, email, now);
        guest.Update(r.FullName, phone, whatsApp, email, r.BirthDate, r.Notes, now);
        db.Set<Guest>().Add(guest);
        audit.Record(new AuditEntry("guest.created", "Guest", guest.Id.ToString(), After: new { source = guest.Source.ToString() }));
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(guest, ct);
    }

    public async Task<Result<GuestDto>> UpdateAsync(Guid id, SaveGuestRequest r, CancellationToken ct)
    {
        var guest = await LoadAsync(id, tracking: true, ct);
        if (guest is null)
        {
            return GuestErrors.NotFound;
        }

        if (guest.IsAnonymized)
        {
            return GuestErrors.Anonymized;
        }

        var (phone, whatsApp, email) = Contacts(r);
        if (await DuplicateAsync(guest.HotelId, guest.Id, phone?.Value, email, ct) is { } duplicate)
        {
            return duplicate;
        }

        if (r.Version is { } version)
        {
            db.Entry(guest).Property(g => g.Version).OriginalValue = version;
        }

        var changed = guest.Update(r.FullName, phone, whatsApp, email, r.BirthDate, r.Notes, time.GetUtcNow());
        if (changed.Count > 0)
        {
            // Только названия полей: персональные данные в журнал не попадают.
            audit.Record(new AuditEntry("guest.updated", "Guest", id.ToString(), After: new { fields = changed }));
        }

        return await SaveAsync(guest, ct);
    }

    public async Task<Result<GuestDto>> SetConsentAsync(Guid id, SetConsentRequest r, CancellationToken ct)
    {
        var guest = await LoadAsync(id, tracking: true, ct);
        if (guest is null)
        {
            return GuestErrors.NotFound;
        }

        if (guest.IsAnonymized)
        {
            return GuestErrors.Anonymized;
        }

        var type = Enum.Parse<ConsentType>(r.Type);
        var source = Enum.Parse<ConsentSource>(r.Source);
        if (guest.RecordConsent(type, r.Granted, source, currentUser.UserId, time.GetUtcNow()))
        {
            audit.Record(new AuditEntry(r.Granted ? "guest.consent.granted" : "guest.consent.revoked", "Guest", id.ToString(),
                After: new { type = type.ToString(), source = source.ToString() }));
        }

        return await SaveAsync(guest, ct);
    }

    public async Task<Result<IReadOnlyList<ConsentHistoryDto>>> GetConsentHistoryAsync(Guid id, CancellationToken ct)
    {
        var guest = await LoadAsync(id, tracking: false, ct);
        if (guest is null)
        {
            return GuestErrors.NotFound;
        }

        var names = await users.GetNamesAsync(guest.Consents.Where(c => c.RecordedBy != null).Select(c => c.RecordedBy!.Value), ct);
        return guest.Consents
            .OrderByDescending(c => c.RecordedAt)
            .Select(c => new ConsentHistoryDto(c.Id, c.Type.ToString(), c.Granted, c.Source.ToString(),
                c.RecordedBy is { } by && names.TryGetValue(by, out var name) ? name : null, c.RecordedAt))
            .ToList();
    }

    public async Task<Result<IReadOnlyList<GuestBookingDto>>> GetBookingsAsync(Guid id, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        if (!await db.Set<Guest>().AnyAsync(g => g.Id == id && g.HotelId == hotelId, ct))
        {
            return GuestErrors.NotFound;
        }

        return (await bookings.ListAsync(id, ct))
            .Select(b => new GuestBookingDto(b.BookingId, b.Number, b.Status.ToString(), b.Kind, b.StartsAt, b.EndsAt, b.RoomNumber, b.TotalAmount, b.CreatedAt))
            .ToList();
    }

    public async Task<Result<GuestDto>> AnonymizeAsync(Guid id, CancellationToken ct)
    {
        var guest = await LoadAsync(id, tracking: true, ct);
        if (guest is null)
        {
            return GuestErrors.NotFound;
        }

        var result = guest.Anonymize(time.GetUtcNow());
        if (result.IsFailure)
        {
            return result.Error;
        }

        // Одна транзакция: карточка и брони (контакт-снимок и история изменений) обезличиваются вместе или никак.
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        await bookings.AnonymizeAsync(id, Guest.AnonymizedName, ct);
        audit.Record(new AuditEntry("guest.anonymized", "Guest", id.ToString()));
        var saved = await SaveAsync(guest, ct);
        if (saved.IsSuccess)
        {
            await transaction.CommitAsync(ct);
        }

        return saved;
    }

    private static IQueryable<Guest> Search(IQueryable<Guest> query, string? search)
    {
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }

        var text = search.Trim();
        var like = $"%{text.ToLowerInvariant()}%";
        // Телефон ищем по цифрам: «0555 12», «+996 555 12…» и «55512» находят одного гостя.
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        var phoneLike = digits.Length >= 3 ? $"%{digits}%" : null;
#pragma warning disable CA1304, CA1311 // ToLower транслируется в SQL lower()
        return query.Where(g => EF.Functions.Like(g.FullName.ToLower(), like)
            || (g.Email != null && EF.Functions.Like(g.Email, like))
            || (phoneLike != null && ((g.Phone != null && EF.Functions.Like(g.Phone, phoneLike)) || (g.WhatsAppPhone != null && EF.Functions.Like(g.WhatsAppPhone, phoneLike)))));
#pragma warning restore CA1304, CA1311
    }

    private static (PhoneNumber? Phone, PhoneNumber? WhatsApp, string? Email) Contacts(SaveGuestRequest r) => (
        string.IsNullOrWhiteSpace(r.Phone) ? null : PhoneNumber.Parse(r.Phone).Value,
        string.IsNullOrWhiteSpace(r.WhatsAppPhone) ? null : PhoneNumber.Parse(r.WhatsAppPhone).Value,
        Guest.NormalizeEmail(r.Email));

    /// <summary>Дубль по телефону или email — ошибка с id найденной карточки, чтобы администратор открыл её.</summary>
    private async Task<Error?> DuplicateAsync(Guid hotelId, Guid? self, string? phone, string? email, CancellationToken ct)
    {
        var active = db.Set<Guest>().AsNoTracking().Where(g => g.HotelId == hotelId && g.AnonymizedAt == null && g.Id != self);
        if (phone is not null && await active.Where(g => g.Phone == phone).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct) is { } byPhone)
        {
            return GuestAppErrors.Duplicate(byPhone, "телефоном");
        }

        if (email is not null && await active.Where(g => g.Email == email).Select(g => (Guid?)g.Id).FirstOrDefaultAsync(ct) is { } byEmail)
        {
            return GuestAppErrors.Duplicate(byEmail, "email");
        }

        return null;
    }

    private async Task<Guest?> LoadAsync(Guid id, bool tracking, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var query = db.Set<Guest>().Include(g => g.Consents).Where(g => g.Id == id && g.HotelId == hotelId);
        return await (tracking ? query : query.AsNoTracking()).FirstOrDefaultAsync(ct);
    }

    private async Task<Result<GuestDto>> SaveAsync(Guest guest, CancellationToken ct)
    {
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        {
            db.ChangeTracker.Clear();
            return GuestAppErrors.ConcurrencyConflict;
        }

        return await ToDtoAsync(guest, ct);
    }

    private async Task<GuestDto> ToDtoAsync(Guest g, CancellationToken ct)
    {
        var stats = (await bookings.StatsAsync([g.Id], ct)).GetValueOrDefault(g.Id);
        var consents = Enum.GetValues<ConsentType>()
            .Select(type => g.CurrentConsent(type) is { } c ? new ConsentStateDto(type.ToString(), c.Granted, c.Source.ToString(), c.RecordedAt) : null)
            .OfType<ConsentStateDto>()
            .ToList();
        return new GuestDto(g.Id, g.FullName, g.Phone, g.WhatsAppPhone, g.Email, g.BirthDate, g.Notes, g.Source.ToString(), g.UserId != null,
            g.IsAnonymized, g.CreatedAt, g.UpdatedAt, ToDto(stats), consents, g.Version);
    }

    private static GuestStatsDto ToDto(GuestBookingStats? s) =>
        s is null ? new GuestStatsDto(0, 0, 0m, null) : new GuestStatsDto(s.BookingsCount, s.VisitsCount, s.BookedAmount, s.LastStayAt);
}
