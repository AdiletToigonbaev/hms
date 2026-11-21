using Hms.SharedKernel;

namespace Hms.Modules.Guests.Contracts;

/// <summary>Краткие сведения о госте для других модулей.</summary>
public sealed record GuestRef(Guid Id, string FullName, string? Phone, string? Email, bool IsAnonymized);

/// <summary>Контакт из брони или регистрации: по нему ищется существующая карточка или создаётся новая.</summary>
public sealed record GuestContact(string FullName, string? Phone, string? Email);

/// <summary>Виды согласий гостя (ТЗ §7, §11; закон КР о персональных данных).</summary>
public enum ConsentType
{
    PersonalData,
    WhatsApp,
}

/// <summary>Карточки гостей для модуля Bookings: найти, выбрать, создать при брони.</summary>
public interface IGuestDirectory
{
    Task<GuestRef?> GetAsync(Guid guestId, CancellationToken ct);

    /// <summary>
    /// Карточка по телефону (иначе по email); если нет — новая. Изменения добавляются в текущую единицу работы
    /// (сохраняет вызывающий модуль вместе с бронью).
    /// </summary>
    Task<Result<GuestRef>> FindOrCreateAsync(GuestContact contact, CancellationToken ct);
}

/// <summary>Регистрация гостя на сайте (модуль Identity): карточка, связь с аккаунтом и согласия — в той же транзакции.</summary>
public interface IGuestRegistration
{
    Task RegisterPortalGuestAsync(Guid userId, GuestContact contact, bool personalDataConsent, bool whatsAppConsent, CancellationToken ct);
}

public static class GuestErrors
{
    public static readonly Error NotFound = Error.NotFound("guest.not_found", "Гость не найден.");
    public static readonly Error Anonymized = Error.BusinessRule("guest.anonymized", "Данные гостя удалены — выберите другого гостя или создайте нового.");
    public static readonly Error ContactRequired = Error.Validation("guest.contact_required", "Укажите телефон или email гостя.");
}
