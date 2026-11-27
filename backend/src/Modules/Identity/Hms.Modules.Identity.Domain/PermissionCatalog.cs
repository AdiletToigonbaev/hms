using Hms.Modules.Identity.Contracts;
using P = Hms.Modules.Identity.Contracts.Permissions;

namespace Hms.Modules.Identity.Domain;

/// <summary>Описание разрешения.</summary>
/// <param name="Code">Код (<c>booking.cancel</c>).</param>
/// <param name="Group">Раздел для группировки в интерфейсе.</param>
/// <param name="Description">Описание на русском.</param>
/// <param name="OwnerOnly">Только владелец: нельзя выдать администратору (матрица прав: «—» у администратора).</param>
public sealed record PermissionDefinition(string Code, string Group, string Description, bool OwnerOnly = false);

/// <summary>
/// Каталог разрешений и права ролей по умолчанию — реализация матрицы
/// docs/architecture/security-architecture.md §3. Каталог синхронизируется в БД при каждом релизе; права ролей
/// после первой установки настраивает владелец (сидер добавляет ролям только новые права каталога).
/// </summary>
public static class PermissionCatalog
{
    public static readonly IReadOnlyList<PermissionDefinition> All =
    [
        new(P.DashboardOwnerView, "Аналитика", "Дашборд владельца: KPI, выручка, прибыль", OwnerOnly: true),
        new(P.DashboardOperationsView, "Аналитика", "Операционный дашборд администратора"),
        new(P.FinanceReportView, "Аналитика", "Финансовые отчёты"),
        new(P.ReportExport, "Аналитика", "Экспорт отчётов в Excel и PDF"),

        new(P.BookingView, "Бронирования", "Просмотр всех бронирований"),
        new(P.BookingCreate, "Бронирования", "Создание бронирований"),
        new(P.BookingEdit, "Бронирования", "Изменение бронирований, подтверждение, No-Show"),
        new(P.BookingCancel, "Бронирования", "Отмена бронирований по правилам"),
        new(P.BookingCancelPenaltyOverride, "Бронирования", "Отмена без штрафа (исключение из правил)"),
        new(P.CalendarView, "Бронирования", "Календарь бронирований"),
        new(P.CheckInPerform, "Заселение", "Заселение (Check-in)"),
        new(P.CheckOutPerform, "Заселение", "Выселение (Check-out)"),

        new(P.RoomManage, "Номера", "Управление номерами и категориями"),
        new(P.RoomStatusChange, "Номера", "Изменение статуса номера"),
        new(P.RateView, "Номера", "Просмотр тарифов и скидок"),
        new(P.RateManage, "Номера", "Управление тарифами, скидками, услугами", OwnerOnly: true),

        new(P.GuestView, "Гости", "Просмотр базы гостей"),
        new(P.GuestManage, "Гости", "Создание и изменение гостей, согласия"),
        new(P.GuestAnonymize, "Гости", "Удаление персональных данных гостя (обезличивание)", OwnerOnly: true),

        new(P.PaymentView, "Финансы", "Просмотр платежей"),
        new(P.PaymentRegister, "Финансы", "Ручная регистрация платежей"),
        new(P.PaymentReverse, "Финансы", "Аннулирование ошибочных платежей"),
        new(P.RefundRequest, "Финансы", "Оформление запроса на возврат"),
        new(P.RefundApprove, "Финансы", "Утверждение возвратов (в пределах лимита)"),
        new(P.ExpenseCreate, "Финансы", "Внесение расходов и просмотр своих"),
        new(P.ExpenseManage, "Финансы", "Просмотр всех расходов, правка, аннулирование", OwnerOnly: true),

        new(P.HousekeepingManage, "Уборка", "Управление задачами уборки и назначение"),
        new(P.HousekeepingInspect, "Уборка", "Проверка уборки (принять/вернуть)"),
        new(P.HousekeepingWork, "Уборка", "Выполнение своих задач уборки"),
        new(P.MaintenanceCreate, "Уборка", "Сообщение о неисправности"),
        new(P.MaintenanceManage, "Уборка", "Обработка заявок о неисправностях"),

        new(P.WhatsAppSend, "Сообщения", "Сообщения гостям: переписка WhatsApp, история, ручная отправка email и WhatsApp"),
        new(P.WhatsAppTemplatesManage, "Сообщения", "Шаблоны уведомлений, время напоминаний и автоответчик", OwnerOnly: true),

        new(P.SettingsManage, "Система", "Настройки гостиницы и правил", OwnerOnly: true),
        new(P.IntegrationsManage, "Система", "Настройка интеграций (банки, WhatsApp, email)", OwnerOnly: true),
        new(P.StaffManage, "Система", "Управление сотрудниками и правами", OwnerOnly: true),
        new(P.AuditView, "Система", "Журнал аудита", OwnerOnly: true),
    ];

    private static readonly Dictionary<string, PermissionDefinition> ByCode = All.ToDictionary(p => p.Code, StringComparer.Ordinal);

    /// <summary>Права администратора по умолчанию (матрица: ✔ и ⚙ со значением «да»).</summary>
    private static readonly string[] AdministratorDefaults =
    [
        P.DashboardOperationsView,
        P.BookingView, P.BookingCreate, P.BookingEdit, P.BookingCancel, P.CalendarView,
        P.CheckInPerform, P.CheckOutPerform,
        P.RoomManage, P.RoomStatusChange, P.RateView,
        P.GuestView, P.GuestManage,
        P.PaymentView, P.PaymentRegister, P.RefundRequest, P.ExpenseCreate,
        P.HousekeepingManage, P.HousekeepingInspect, P.MaintenanceCreate, P.MaintenanceManage,
        P.WhatsAppSend,
    ];

    private static readonly string[] HousekeeperDefaults = [P.HousekeepingWork, P.MaintenanceCreate];

    public static bool Exists(string code) => ByCode.ContainsKey(code);

    public static PermissionDefinition? Find(string code) => ByCode.GetValueOrDefault(code);

    /// <summary>Права роли по умолчанию. Владелец получает все разрешения каталога.</summary>
    public static IReadOnlyList<string> DefaultsFor(string role) => role switch
    {
        Roles.Owner => All.Select(p => p.Code).ToList(),
        Roles.Administrator => AdministratorDefaults,
        Roles.Housekeeper => HousekeeperDefaults,
        _ => [],
    };

    /// <summary>
    /// Права, которые можно выдать уборщику: только работа с уборкой и заявками. Финансы и данные гостей уборщику
    /// недоступны (инвариант проекта), остальные экраны персонала ему не открываются.
    /// </summary>
    private static readonly HashSet<string> HousekeeperAllowed = new([P.HousekeepingWork, P.MaintenanceCreate], StringComparer.Ordinal);

    /// <summary>Роли, права которых настраивает владелец (своя роль владельца — всегда все права).</summary>
    public static readonly IReadOnlyList<string> ConfigurableRoles = [Roles.Administrator, Roles.Housekeeper];

    /// <summary>Права с лимитом суммы (лимит задаётся роли и/или сотруднику; пусто — без лимита).</summary>
    public static readonly IReadOnlySet<string> LimitedPermissions = new HashSet<string>([P.RefundApprove], StringComparer.Ordinal);

    /// <summary>Можно ли выдать разрешение роли или сотруднику этой роли.</summary>
    public static bool IsAllowedFor(string role, string code) =>
        ByCode.TryGetValue(code, out var p) && role switch
        {
            Roles.Administrator => !p.OwnerOnly,
            Roles.Housekeeper => HousekeeperAllowed.Contains(code),
            _ => false,
        };

    /// <summary>Можно ли владельцу выдать/отозвать это разрешение у администратора индивидуально.</summary>
    public static bool IsConfigurableForAdministrator(string code) => IsAllowedFor(Roles.Administrator, code);
}
