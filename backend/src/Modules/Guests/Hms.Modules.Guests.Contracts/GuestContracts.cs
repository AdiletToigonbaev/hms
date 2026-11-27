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

    /// <summary>Карточки по списку (отчёты владельца).</summary>
    Task<IReadOnlyDictionary<Guid, GuestRef>> GetManyAsync(IReadOnlyCollection<Guid> guestIds, CancellationToken ct);

    /// <summary>
    /// Карточка по телефону (иначе по email); если нет — новая. Изменения добавляются в текущую единицу работы
    /// (сохраняет вызывающий модуль вместе с бронью).
    /// </summary>
    Task<Result<GuestRef>> FindOrCreateAsync(GuestContact contact, CancellationToken ct);
}

/// <summary>
/// Гость, вошедший на сайт: его карточка (по связи аккаунта с карточкой). Null — пользователь не гость,
/// карточки нет или данные гостя удалены. Основа проверки «только свои брони и платежи» (ТЗ §3.4).
/// </summary>
public interface ICurrentGuest
{
    Task<GuestRef?> GetAsync(CancellationToken ct);
}

/// <summary>Согласия гостя в кабинете: действующее значение и когда дано/отозвано.</summary>
public sealed record PortalConsents(bool PersonalData, DateTimeOffset? PersonalDataAt, bool WhatsApp, DateTimeOffset? WhatsAppAt);

/// <summary>Аккаунт гостя на сайте (модуль Identity): профиль, согласия, удаление аккаунта.</summary>
public interface IGuestAccount
{
    /// <summary>Имя и телефон из профиля — в карточку гостя. Добавляется в текущую единицу работы (сохраняет вызывающий).</summary>
    Task SyncProfileAsync(Guid userId, string fullName, string? phone, CancellationToken ct);

    Task<PortalConsents?> GetConsentsAsync(Guid userId, CancellationToken ct);

    Task<Result<PortalConsents>> SetWhatsAppConsentAsync(Guid userId, bool granted, CancellationToken ct);

    /// <summary>
    /// Удаление аккаунта: карточка и брони гостя обезличиваются. Нельзя при действующих бронях.
    /// Выполняется сразу — вызывайте внутри транзакции вызывающего.
    /// </summary>
    Task<Result> RemoveAsync(Guid userId, CancellationToken ct);
}

/// <summary>Регистрация гостя на сайте (модуль Identity): карточка, связь с аккаунтом и согласия — в той же транзакции.</summary>
public interface IGuestRegistration
{
    /// <returns>Id карточки гостя.</returns>
    Task<Guid> RegisterPortalGuestAsync(Guid userId, GuestContact contact, bool personalDataConsent, bool whatsAppConsent, CancellationToken ct);
}

public static class GuestErrors
{
    public static readonly Error NotFound = Error.NotFound("guest.not_found", "Гость не найден.");
    public static readonly Error Anonymized = Error.BusinessRule("guest.anonymized", "Данные гостя удалены — выберите другого гостя или создайте нового.");
    public static readonly Error ContactRequired = Error.Validation("guest.contact_required", "Укажите телефон или email гостя.");
}

/// <summary>Контакты и согласие гостя для сообщений (модуль Messaging).</summary>
/// <param name="WhatsAppNumber">Номер WhatsApp (E.164): отдельный, если указан, иначе основной телефон.</param>
public sealed record GuestMessagingProfile(Guid Id, string FullName, string? WhatsAppNumber, string? Email, bool WhatsAppConsent, bool IsAnonymized);

/// <summary>Сведения о госте для модуля Messaging: куда писать и можно ли в WhatsApp; отписка словом «стоп».</summary>
public interface IGuestMessaging
{
    Task<GuestMessagingProfile?> GetAsync(Guid guestId, CancellationToken ct);

    Task<IReadOnlyDictionary<Guid, GuestMessagingProfile>> GetManyAsync(IReadOnlyCollection<Guid> guestIds, CancellationToken ct);

    /// <summary>Гость по номеру WhatsApp (входящее сообщение). Обезличенные не находятся.</summary>
    Task<GuestMessagingProfile?> FindByWhatsAppAsync(string phoneE164, CancellationToken ct);

    /// <summary>Отзыв согласия на WhatsApp (гость написал «стоп»). Добавляется в текущую единицу работы. True — согласие было.</summary>
    Task<bool> RevokeWhatsAppConsentAsync(Guid guestId, CancellationToken ct);
}
