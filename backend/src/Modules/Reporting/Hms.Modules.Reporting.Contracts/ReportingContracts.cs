namespace Hms.Modules.Reporting.Contracts;

/// <summary>Виды отчётов (ТЗ §12). Модуль Reporting только читает данные других модулей через их контракты.</summary>
public static class ReportTypes
{
    public const string Financial = "financial";
    public const string Bookings = "bookings";
    public const string Guests = "guests";
    public const string Rooms = "rooms";
    public const string Staff = "staff";
    public const string Payments = "payments";
    public const string Refunds = "refunds";
    public const string Expenses = "expenses";

    public static readonly IReadOnlyList<string> All = [Financial, Bookings, Guests, Rooms, Staff, Payments, Refunds, Expenses];
}
