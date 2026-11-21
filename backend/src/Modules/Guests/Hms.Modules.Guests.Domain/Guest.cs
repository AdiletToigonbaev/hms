using Hms.Modules.Guests.Contracts;
using Hms.SharedKernel;

namespace Hms.Modules.Guests.Domain;

/// <summary>Откуда появилась карточка гостя.</summary>
public enum GuestSource
{
    /// <summary>Создал администратор в разделе «Гости».</summary>
    Admin,

    /// <summary>Создана при оформлении брони (по контакту гостя).</summary>
    Booking,

    /// <summary>Гость зарегистрировался на сайте.</summary>
    Portal,
}

/// <summary>Откуда получено согласие (ТЗ: согласие фиксируется с источником).</summary>
public enum ConsentSource
{
    Portal,
    FrontDesk,
    Phone,
    WhatsApp,
    Written,
}

/// <summary>
/// Гость (ТЗ §7, CRM). Документы не хранятся (решение заказчика 2026-11-20).
/// Удаление — обезличивание: персональные данные стираются, цифры по броням остаются.
/// </summary>
public sealed class Guest
{
    public const string AnonymizedName = "Гость удалён";

    private readonly List<GuestConsent> _consents = [];

    private Guest()
    {
        FullName = string.Empty;
    }

    public Guid Id { get; private set; }

    public Guid HotelId { get; private set; }

    /// <summary>Аккаунт гостя на сайте (если регистрировался).</summary>
    public Guid? UserId { get; private set; }

    public string FullName { get; private set; }

    /// <summary>Телефон в формате E.164.</summary>
    public string? Phone { get; private set; }

    /// <summary>Номер WhatsApp, если отличается от телефона (E.164).</summary>
    public string? WhatsAppPhone { get; private set; }

    /// <summary>Email в нижнем регистре.</summary>
    public string? Email { get; private set; }

    public DateOnly? BirthDate { get; private set; }

    /// <summary>Внутренние заметки администратора — гость их не видит.</summary>
    public string? Notes { get; private set; }

    public GuestSource Source { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? AnonymizedAt { get; private set; }

    public uint Version { get; private set; }

    public IReadOnlyList<GuestConsent> Consents => _consents;

    public bool IsAnonymized => AnonymizedAt is not null;

    public static Guest Create(Guid hotelId, GuestSource source, string fullName, PhoneNumber? phone, string? email, DateTimeOffset now)
    {
        var guest = new Guest
        {
            Id = Guid.CreateVersion7(),
            HotelId = hotelId,
            Source = source,
            CreatedAt = now,
        };
        guest.SetContact(fullName, phone, email, now);
        return guest;
    }

    /// <summary>Изменяет данные; возвращает названия изменённых полей (для аудита без самих данных).</summary>
    public IReadOnlyList<string> Update(string fullName, PhoneNumber? phone, PhoneNumber? whatsApp, string? email, DateOnly? birthDate, string? notes, DateTimeOffset now)
    {
        var changed = new List<string>();
        void Track(string name, object? before, object? after)
        {
            if (!Equals(before, after))
            {
                changed.Add(name);
            }
        }

        var name = fullName.Trim();
        var normalizedEmail = NormalizeEmail(email);
        var normalizedNotes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        Track("fullName", FullName, name);
        Track("phone", Phone, phone?.Value);
        Track("whatsAppPhone", WhatsAppPhone, whatsApp?.Value);
        Track("email", Email, normalizedEmail);
        Track("birthDate", BirthDate, birthDate);
        Track("notes", Notes, normalizedNotes);

        FullName = name;
        Phone = phone?.Value;
        WhatsAppPhone = whatsApp?.Value;
        Email = normalizedEmail;
        BirthDate = birthDate;
        Notes = normalizedNotes;
        if (changed.Count > 0)
        {
            UpdatedAt = now;
        }

        return changed;
    }

    /// <summary>Дополняет пустые контакты из брони или регистрации (существующие данные не затираются).</summary>
    public void FillMissing(PhoneNumber? phone, string? email, DateTimeOffset now)
    {
        var changed = false;
        if (Phone is null && phone is { } p)
        {
            Phone = p.Value;
            changed = true;
        }

        if (Email is null && NormalizeEmail(email) is { } e)
        {
            Email = e;
            changed = true;
        }

        if (changed)
        {
            UpdatedAt = now;
        }
    }

    public void LinkUser(Guid userId, DateTimeOffset now)
    {
        UserId = userId;
        UpdatedAt = now;
    }

    /// <summary>Фиксирует согласие или его отзыв. Повтор того же состояния не пишется.</summary>
    public bool RecordConsent(ConsentType type, bool granted, ConsentSource source, Guid? by, DateTimeOffset now)
    {
        if (CurrentConsent(type)?.Granted == granted)
        {
            return false;
        }

        _consents.Add(new GuestConsent(Id, type, granted, source, by, now));
        UpdatedAt = now;
        return true;
    }

    public GuestConsent? CurrentConsent(ConsentType type) =>
        _consents.Where(c => c.Type == type).OrderByDescending(c => c.RecordedAt).ThenByDescending(c => c.Id).FirstOrDefault();

    /// <summary>
    /// Обезличивание по решению владельца: стираются ФИО, контакты, дата рождения и заметки.
    /// История согласий остаётся (подтверждение законности прошлой обработки), связь с бронями — тоже.
    /// </summary>
    public Result Anonymize(DateTimeOffset now)
    {
        if (IsAnonymized)
        {
            return GuestErrors.Anonymized;
        }

        if (UserId is not null)
        {
            return Error.BusinessRule("guest.has_account", "У гостя есть аккаунт на сайте — обезличивание аккаунта появится вместе с личным кабинетом (Sprint 8).");
        }

        FullName = AnonymizedName;
        Phone = null;
        WhatsAppPhone = null;
        Email = null;
        BirthDate = null;
        Notes = null;
        AnonymizedAt = now;
        UpdatedAt = now;
        return Result.Success();
    }

    public static string? NormalizeEmail(string? email) => string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();

    private void SetContact(string fullName, PhoneNumber? phone, string? email, DateTimeOffset now)
    {
        FullName = fullName.Trim();
        Phone = phone?.Value;
        Email = NormalizeEmail(email);
        UpdatedAt = now;
    }
}

/// <summary>Запись о согласии или его отзыве. Только добавление.</summary>
public sealed class GuestConsent
{
    private GuestConsent()
    {
    }

    public GuestConsent(Guid guestId, ConsentType type, bool granted, ConsentSource source, Guid? recordedBy, DateTimeOffset at)
    {
        Id = Guid.CreateVersion7();
        GuestId = guestId;
        Type = type;
        Granted = granted;
        Source = source;
        RecordedBy = recordedBy;
        RecordedAt = at;
    }

    public Guid Id { get; private set; }

    public Guid GuestId { get; private set; }

    public ConsentType Type { get; private set; }

    public bool Granted { get; private set; }

    public ConsentSource Source { get; private set; }

    public Guid? RecordedBy { get; private set; }

    public DateTimeOffset RecordedAt { get; private set; }
}
