namespace Hms.Modules.Identity.Contracts;

/// <summary>
/// Коды разрешений (docs/architecture/security-architecture.md §3). Проверяются на backend
/// политикой <c>RequirePermission(Permissions.X)</c>. Описания и права ролей по умолчанию — в PermissionCatalog (Domain).
/// </summary>
public static class Permissions
{
    public const string DashboardOwnerView = "dashboard.owner.view";
    public const string DashboardOperationsView = "dashboard.operations.view";

    public const string BookingView = "booking.view";
    public const string BookingCreate = "booking.create";
    public const string BookingEdit = "booking.edit";
    public const string BookingCancel = "booking.cancel";
    public const string BookingCancelPenaltyOverride = "booking.cancel.penalty_override";
    public const string CheckInPerform = "checkin.perform";
    public const string CheckOutPerform = "checkout.perform";
    public const string CalendarView = "calendar.view";

    public const string RoomManage = "room.manage";
    public const string RoomStatusChange = "room.status.change";
    public const string RateView = "rate.view";
    public const string RateManage = "rate.manage";

    public const string GuestView = "guest.view";
    public const string GuestManage = "guest.manage";
    public const string GuestDocumentsView = "guest.documents.view";
    public const string GuestDocumentsManage = "guest.documents.manage";

    public const string PaymentView = "payment.view";
    public const string PaymentRegister = "payment.register";
    public const string PaymentReverse = "payment.reverse";
    public const string RefundRequest = "refund.request";
    public const string RefundApprove = "refund.approve";
    public const string ExpenseCreate = "expense.create";
    public const string ExpenseManage = "expense.manage";
    public const string FinanceReportView = "finance.report.view";
    public const string ReportExport = "report.export";

    public const string HousekeepingManage = "housekeeping.manage";
    public const string HousekeepingInspect = "housekeeping.inspect";
    public const string HousekeepingWork = "housekeeping.work";
    public const string MaintenanceCreate = "maintenance.create";
    public const string MaintenanceManage = "maintenance.manage";

    public const string WhatsAppSend = "whatsapp.send";
    public const string WhatsAppTemplatesManage = "whatsapp.templates.manage";

    public const string SettingsManage = "settings.manage";
    public const string IntegrationsManage = "integrations.manage";
    public const string StaffManage = "staff.manage";
    public const string AuditView = "audit.view";
}
