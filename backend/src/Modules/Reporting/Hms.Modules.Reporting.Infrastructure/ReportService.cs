using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Finance.Contracts;
using Hms.Modules.Guests.Contracts;
using Hms.Modules.Housekeeping.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Reporting.Application;
using Hms.Modules.Reporting.Contracts;
using Hms.Modules.Reporting.Domain;
using Hms.SharedKernel;

namespace Hms.Modules.Reporting.Infrastructure;

/// <summary>
/// Отчёты ТЗ §12 по фактическим данным. Каждый отчёт — таблица (колонки, строки, итоги) + показатели за период.
/// Фильтры: период, категория номера, способ оплаты, статус брони, сотрудник — там, где они имеют смысл.
/// </summary>
internal sealed class ReportService(
    ReportData data,
    IBookingCharges charges,
    IFinanceFacts finance,
    IGuestDirectory guests,
    IHousekeepingFacts housekeeping,
    IUserDirectory users) : IReportService
{
    private static readonly BookingStatus[] Sold = [BookingStatus.Confirmed, BookingStatus.CheckedIn, BookingStatus.CheckedOut];

    public async Task<Result<ReportTable>> BuildAsync(string type, ReportQuery q, CancellationToken ct)
    {
        if (!ReportTypes.All.Contains(type))
        {
            return ReportErrors.UnknownType;
        }

        var range = data.Resolve(q.From, q.To, q.Period, q.Date);
        if (range.IsFailure)
        {
            return range.Error;
        }

        if (!ReportData.HoursValid(range.Value.From, range.Value.To, q.FromHour, q.ToHour))
        {
            return ReportErrors.HoursInvalid;
        }

        var d = await data.LoadAsync(range.Value.From, range.Value.To, q.RoomTypeId, q.PaymentMethod, ct, q.FromHour, q.ToHour);
        var filters = d.Filters.ToList();
        BookingStatus? status = Enum.TryParse<BookingStatus>(q.BookingStatus, out var s) ? s : null;
        if (status is { } st && type == ReportTypes.Bookings)
        {
            filters.Add($"Статус брони: {Labels.Status(st)}");
        }

        if (q.StaffId is { } staffId)
        {
            filters.Add($"Сотрудник: {(await users.GetNamesAsync([staffId], ct)).GetValueOrDefault(staffId) ?? "—"}");
        }

        var report = type switch
        {
            ReportTypes.Financial => Financial(d),
            ReportTypes.Bookings => await BookingsAsync(d, status, q.StaffId, ct),
            ReportTypes.Guests => await GuestsAsync(d, ct),
            ReportTypes.Rooms => await RoomsAsync(d, ct),
            ReportTypes.Staff => await StaffAsync(d, q.StaffId, ct),
            ReportTypes.Payments => await PaymentsAsync(d, q.StaffId, ct),
            ReportTypes.Refunds => await RefundsAsync(d, q.StaffId, ct),
            _ => await ExpensesAsync(d, q.StaffId, ct),
        };
        return report with { Filters = filters };
    }

    private static ReportColumn Col(string key, string title, string type = "text") => new(key, title, type);

    private static ReportTable Table(PeriodData d, string type, string title, IReadOnlyList<ReportSummaryItem> summary, IReadOnlyList<ReportColumn> columns,
        IReadOnlyList<IReadOnlyList<object?>> rows, IReadOnlyList<object?>? totals, string? note = null) =>
        new(type, title, d.From, d.To, d.Filters, summary, columns, rows, totals, note, d.FromHour, d.ToHour);

    /// <summary>Финансовый отчёт за день/неделю/месяц/год: показатели и разбивка по дням.</summary>
    private ReportTable Financial(PeriodData d)
    {
        var k = data.Kpi(d);
        var daily = data.Daily(d);
        var refundsByDay = d.Refunds.Where(r => r.Status == "Paid" && r.PaidAt >= d.Start && r.PaidAt < d.End)
            .GroupBy(r => d.Days.First(x => r.PaidAt >= x.Start && r.PaidAt < x.End).Date)
            .ToDictionary(g => g.Key, g => g.Sum(r => r.Amount));
        var summary = new List<ReportSummaryItem>
        {
            new("Начисленная выручка", k.AccruedRevenue, "money", "Сколько гостиница заработала по броням за период: стоимость проживания, услуг и продлений, приходящаяся на эти дни и часы, плюс начисленные штрафы. Не зависит от того, оплачено ли. Учитываются подтверждённые, заселённые и завершённые брони.", "Бронь на 2 ночи по 3 000 сом (29–30 ноября): в отчёте за 29 ноября — 3 000 сом, за 30-е — ещё 3 000."),
            new("Поступления", k.CashIn, "money", "Деньги, которые фактически приняли за период: все платежи по дате оплаты минус аннулированные (ошибочные) платежи.", "Приняли 5 000 сом наличными и 2 000 переводом, один платёж на 500 аннулировали — поступления 6 500 сом."),
            new("Возвраты", k.RefundsPaid, "money", "Деньги, выданные гостям за период — по дате выплаты. Запрошенные, но ещё не выданные возвраты сюда не входят.", "Возврат 1 000 сом одобрили 20-го, а выдали 21-го — он попадает в отчёт за 21-е."),
            new("Чистые поступления", k.NetRevenue, "money", "Поступления минус возвраты: сколько денег от гостей осталось у гостиницы за период.", "Поступления 6 500 сом, возвраты 1 000 — чистые поступления 5 500 сом."),
            new("Расходы", k.Expenses, "money", "Расходы гостиницы за период из раздела «Расходы», без аннулированных. Учитываются по дате расхода, за целые дни (у расхода нет времени).", "Зарплата 30 000 сом и продукты 4 000 — расходы 34 000 сом."),
            new("Прибыль (по поступлениям)", k.NetProfit, "money", "Реальные деньги: чистые поступления минус расходы. Показывает, на сколько выросли деньги гостиницы за период.", "Чистые поступления 55 000 сом, расходы 34 000 — прибыль 21 000 сом."),
            new("Прибыль (по начислению)", k.AccruedProfit, "money", "Заработанное по броням минус расходы — независимо от того, заплатили ли гости. Если она заметно больше прибыли по поступлениям, у гостей есть долги или оплата ещё впереди.", "Начислено 60 000 сом, расходы 34 000 — прибыль 26 000, даже если часть броней оплатят позже."),
            new("Загрузка", k.Occupancy, "percent", "Какая доля времени номеров продана: занятые номеро-часы по подтверждённым, заселённым и завершённым броням от всех часов доступных номеров за период. Номера «вне продажи» не считаются.", "10 номеров на сутки — 240 номеро-часов; продано 60 часов — загрузка 25 %."),
            new("Средняя цена часа", k.AvgHourPrice, "money", "Средняя цена одного проданного часа по почасовым броням: выручка за проживание (без услуг, с учётом скидок) / проданные часы.", "3 брони по 2 часа за 1 000, 1 200 и 800 сом: 3 000 / 6 ч = 500 сом за час."),
            new("Выручка на номер-час", k.RevenuePerRoomHour, "money", "Сколько в среднем приносит один доступный номер за час — с учётом простоя: выручка за проживание по всем броням / (номера × часы периода).", "10 номеров за сутки (240 номеро-часов) принесли 24 000 сом — 100 сом на номер-час."),
            new("ADR (средняя цена ночи)", k.Adr, "money", "Средняя цена проданной ночи по посуточным броням: выручка за проживание / проданные номеро-ночи. Почасовые брони сюда не входят.", "Продали 4 ночи за 14 000 сом — ADR 3 500 сом."),
            new("RevPAR", k.RevPar, "money", "Выручка посуточного проживания на один доступный номер в сутки. Учитывает и цену, и загрузку: RevPAR = ADR × доля проданных ночей.", "10 номеров, одни сутки, посуточная выручка 14 000 сом — RevPAR 1 400 сом."),
            new("Брони", k.TotalBookings, "int", "Брони, проживание по которым начинается в периоде, — включая отменённые и неявки. Истёкшие неподтверждённые брони не считаются.", "Бронь оформлена 20-го на 25-е — она в отчёте за 25-е."),
            new("Отмены", k.CancelledBookings, "int", "Сколько броней с началом в периоде отменено — гостем на сайте или администратором.", "Из 40 броней недели отменили 3 — отмен 3 (7,5 %)."),
        };
        var rows = daily.Select(p =>
        {
            var refunds = refundsByDay.GetValueOrDefault(p.Date);
            return (IReadOnlyList<object?>)[ReportData.Format(p.Date), p.AccruedRevenue, p.CashIn, refunds, p.Expenses, p.CashIn - refunds - p.Expenses, p.Occupancy, p.Arrivals];
        }).ToList();
        return Table(d, ReportTypes.Financial, "Финансовый отчёт", summary,
            [Col("date", "Дата", "date"), Col("accrued", "Начислено", "money"), Col("cashIn", "Поступления", "money"), Col("refunds", "Возвраты", "money"),
                Col("expenses", "Расходы", "money"), Col("profit", "Прибыль (поступления)", "money"), Col("occupancy", "Загрузка", "percent"), Col("arrivals", "Заезды", "int")],
            rows,
            ["Итого", k.AccruedRevenue, k.CashIn, k.RefundsPaid, k.Expenses, k.NetProfit, k.Occupancy, daily.Sum(p => p.Arrivals)],
            d.ExpensesApply ? null : "С фильтром по категории расходы гостиницы не учитываются: они не делятся по категориям номеров.");
    }

    /// <summary>Брони с началом проживания в периоде.</summary>
    private async Task<ReportTable> BookingsAsync(PeriodData d, BookingStatus? status, Guid? staffId, CancellationToken ct)
    {
        var list = d.Bookings.Where(b => b.StartsAt >= d.Start && b.StartsAt < d.End && b.Status != BookingStatus.Expired)
            .Where(b => status is null || b.Status == status)
            .Where(b => staffId is null || b.CreatedBy == staffId)
            .OrderBy(b => b.StartsAt).ToList();
        var names = await users.GetNamesAsync(list.Select(b => b.CreatedBy).OfType<Guid>().Distinct(), ct);
        var types = d.Rooms.GroupBy(r => r.RoomTypeId).ToDictionary(g => g.Key, g => g.First().RoomTypeName);
        var rooms = d.Rooms.ToDictionary(r => r.Id, r => r.Number);
        var rows = list.Select(b => (IReadOnlyList<object?>)[
            b.Number, Labels.Kind(b.Kind), types.GetValueOrDefault(b.RoomTypeId) ?? "—", rooms.GetValueOrDefault(b.RoomId) ?? "—",
            data.Format(b.StartsAt), data.Format(b.EndsAt), b.Kind == "Hourly" ? b.Hours ?? 0 : b.Nights,
            Labels.Status(b.Status), Labels.Source(b.Source), b.TotalAmount, b.PenaltyAmount,
            b.Source == "Web" ? "Сайт (гость)" : b.CreatedBy is { } c ? names.GetValueOrDefault(c) ?? "—" : "—"]).ToList();
        var sold = list.Where(b => Sold.Contains(b.Status)).ToList();
        return Table(d, ReportTypes.Bookings, "Отчёт по бронированиям",
            [
                new("Броней", list.Count, "int"),
                new("Состоялись или действуют", sold.Count, "int"),
                new("Отменены", list.Count(b => b.Status == BookingStatus.Cancelled), "int"),
                new("Неявки", list.Count(b => b.Status == BookingStatus.NoShow), "int"),
                new("Почасовых", list.Count(b => b.Kind == "Hourly"), "int"),
                new("С сайта", list.Count(b => b.Source == "Web"), "int"),
                new("Стоимость состоявшихся", sold.Sum(b => b.TotalAmount), "money"),
                new("Штрафы", list.Sum(b => b.PenaltyAmount), "money"),
            ],
            [Col("number", "Бронь"), Col("kind", "Вид"), Col("roomType", "Категория"), Col("room", "Номер"), Col("starts", "Начало", "datetime"),
                Col("ends", "Окончание", "datetime"), Col("units", "Часов/ночей", "int"), Col("status", "Статус"), Col("source", "Источник"),
                Col("total", "Стоимость", "money"), Col("penalty", "Штраф", "money"), Col("createdBy", "Оформил")],
            rows,
            ["Итого", null, null, null, null, null, null, null, null, list.Sum(b => b.TotalAmount), list.Sum(b => b.PenaltyAmount), null]);
    }

    /// <summary>Гости с бронями в периоде: брони, посещения, начислено, оплачено (за всё время по этим броням).</summary>
    private async Task<ReportTable> GuestsAsync(PeriodData d, CancellationToken ct)
    {
        var list = d.Bookings.Where(b => b.GuestId is not null && b.StartsAt >= d.Start && b.StartsAt < d.End && b.Status != BookingStatus.Expired).ToList();
        var cards = await guests.GetManyAsync(list.Select(b => b.GuestId!.Value).Distinct().ToList(), ct);
        var received = list.Count == 0 ? new Dictionary<Guid, decimal>() : await finance.NetReceivedAsync(list.Select(b => b.BookingId).ToList(), ct);
        var rows = list.GroupBy(b => b.GuestId!.Value)
            .Select(g =>
            {
                var card = cards.GetValueOrDefault(g.Key);
                var sold = g.Where(b => Sold.Contains(b.Status)).ToList();
                return new
                {
                    Name = card?.FullName ?? "—",
                    Row = (IReadOnlyList<object?>)[
                        card?.FullName ?? "—", card?.Phone, card?.Email, g.Count(),
                        g.Count(b => b.Status is BookingStatus.CheckedIn or BookingStatus.CheckedOut),
                        g.Count(b => b.Status == BookingStatus.Cancelled), g.Count(b => b.Status == BookingStatus.NoShow),
                        sold.Sum(b => b.TotalAmount) + g.Where(b => !Sold.Contains(b.Status)).Sum(b => b.PenaltyAmount),
                        g.Sum(b => received.GetValueOrDefault(b.BookingId))],
                    Revenue = sold.Sum(b => b.TotalAmount),
                };
            })
            .OrderByDescending(x => x.Revenue).ThenBy(x => x.Name, StringComparer.Ordinal)
            .Select(x => x.Row).ToList();
        return Table(d, ReportTypes.Guests, "Отчёт по гостям",
            [
                new("Гостей", rows.Count, "int"),
                new("Повторных (2+ брони)", rows.Count(r => (int)r[3]! >= 2), "int"),
                new("Броней", list.Count, "int"),
            ],
            [Col("name", "Гость"), Col("phone", "Телефон"), Col("email", "Email"), Col("bookings", "Броней", "int"), Col("visits", "Посещений", "int"),
                Col("cancelled", "Отмен", "int"), Col("noShow", "Неявок", "int"), Col("accrued", "Начислено", "money"), Col("paid", "Оплачено", "money")],
            rows,
            ["Итого", null, null, list.Count, null, null, null, rows.Sum(r => (decimal)r[7]!), rows.Sum(r => (decimal)r[8]!)]);
    }

    /// <summary>Номера: брони, занятые часы, загрузка, начислено, уборки.</summary>
    private async Task<ReportTable> RoomsAsync(PeriodData d, CancellationToken ct)
    {
        var cleanings = (await housekeeping.ListAsync(d.Start, d.End, ct)).Where(c => c.CompletedAt >= d.Start && c.CompletedAt < d.End).ToList();
        var periodHours = (decimal)(d.End - d.Start).TotalHours;
        var rows = d.Rooms.OrderBy(r => r.Number, StringComparer.Ordinal).Select(room =>
        {
            // По отрезкам: переселённый гость считается в каждом номере за своё время, выручка делится по времени.
            var segments = d.Bookings
                .SelectMany(b => (b.Segments is { Count: > 0 } list ? list : [new BookingRoomSegment(b.RoomId, b.StartsAt, b.EndsAt)])
                    .Where(x => x.RoomId == room.Id).Select(x => (Booking: b, Segment: x)))
                .ToList();
            var stays = segments.Select(x => x.Booking).Distinct().ToList();
            var hours = segments.Where(x => Sold.Contains(x.Booking.Status))
                .Sum(x => (decimal)Kpi.Overlap(x.Segment.StartsAt, x.Segment.EndsAt, d.Start, d.End).TotalHours);
            var accrued = segments.Sum(x =>
            {
                var whole = (decimal)Kpi.Overlap(x.Booking.StartsAt, x.Booking.EndsAt, d.Start, d.End).TotalSeconds;
                var part = (decimal)Kpi.Overlap(x.Segment.StartsAt, x.Segment.EndsAt, d.Start, d.End).TotalSeconds;
                var share = x.Booking.Segments is not { Count: > 1 } ? 1m : whole == 0 ? 0m : part / whole;
                return Kpi.Accrued([data.ToStay(x.Booking)], d.Start, d.End, d.Days) * share;
            });
            var roomCleanings = cleanings.Where(c => c.RoomId == room.Id).ToList();
            var minutes = roomCleanings.Where(c => c.StartedAt is not null && c.CompletedAt is not null)
                .Select(c => (decimal)(c.CompletedAt!.Value - c.StartedAt!.Value).TotalMinutes).ToList();
            return (IReadOnlyList<object?>)[
                room.Number, room.RoomTypeName, room.Status == Rooms.Contracts.RoomStatus.OutOfService ? "Вне продажи" : null,
                stays.Count(b => b.StartsAt >= d.Start && b.StartsAt < d.End && b.Status != BookingStatus.Expired && b.Status != BookingStatus.Cancelled),
                Math.Round(hours, 1),
                room.Status == Rooms.Contracts.RoomStatus.OutOfService || periodHours == 0 ? 0m : Math.Round(hours / periodHours, 4),
                Math.Round(accrued, 2),
                roomCleanings.Count,
                minutes.Count == 0 ? null : Math.Round(minutes.Average(), 0)];
        }).ToList();
        var k = data.Kpi(d);
        return Table(d, ReportTypes.Rooms, "Отчёт по номерам",
            [
                new("Доступно номеров", d.AvailableRooms, "int"),
                new("Загрузка", k.Occupancy, "percent"),
                new("Продано номеро-часов (почасовые)", k.HoursSold, "number"),
                new("Продано номеро-ночей (посуточные)", k.RoomNightsSold, "number"),
                new("Оборот номера в день", k.Turnover, "number", "Брони на номер в сутки"),
                new("Уборок", cleanings.Count, "int"),
            ],
            [Col("room", "Номер"), Col("type", "Категория"), Col("note", "Отметка"), Col("bookings", "Броней", "int"), Col("hours", "Занято, ч", "number"),
                Col("occupancy", "Загрузка", "percent"), Col("accrued", "Начислено", "money"), Col("cleanings", "Уборок", "int"), Col("cleaningMinutes", "Уборка, мин (сред.)", "int")],
            rows,
            ["Итого", null, null, rows.Sum(r => (int)r[3]!), rows.Sum(r => (decimal)r[4]!), k.Occupancy, rows.Sum(r => (decimal)r[6]!), cleanings.Count, null]);
    }

    /// <summary>Сотрудники: брони, заселения, выселения, отмены, принятые платежи, решения по возвратам, уборки.</summary>
    private async Task<ReportTable> StaffAsync(PeriodData d, Guid? staffId, CancellationToken ct)
    {
        var staff = new Dictionary<Guid, (string Name, string Role)>();
        foreach (var role in new[] { Roles.Owner, Roles.Administrator, Roles.Housekeeper })
        {
            foreach (var (id, name) in await users.ListActiveInRoleAsync(role, ct))
            {
                staff.TryAdd(id, (name, Labels.Role(role)));
            }
        }

        var cleanings = (await housekeeping.ListAsync(d.Start, d.End, ct)).Where(c => c.CompletedAt >= d.Start && c.CompletedAt < d.End).ToList();
        bool In(DateTimeOffset? at) => at >= d.Start && at < d.End;
        var rows = staff.Where(s => staffId is null || s.Key == staffId).OrderBy(s => s.Value.Role, StringComparer.Ordinal).ThenBy(s => s.Value.Name, StringComparer.Ordinal).Select(s =>
        {
            var id = s.Key;
            var payments = d.Payments.Where(p => p.RegisteredBy == id && p.Type != "Reversal").ToList();
            var mine = cleanings.Where(c => c.AssignedTo == id).ToList();
            var minutes = mine.Where(c => c.StartedAt is not null).Select(c => (decimal)(c.CompletedAt!.Value - c.StartedAt!.Value).TotalMinutes).ToList();
            return (IReadOnlyList<object?>)[
                s.Value.Name, s.Value.Role,
                d.Bookings.Count(b => b.CreatedBy == id && In(b.CreatedAt)),
                d.Bookings.Count(b => b.CheckedInBy == id && In(b.ActualCheckInAt)),
                d.Bookings.Count(b => b.CheckedOutBy == id && In(b.ActualCheckOutAt)),
                d.Bookings.Count(b => b.CancelledBy == id && In(b.CancelledAt)),
                payments.Count, payments.Sum(p => p.Amount),
                d.Refunds.Count(r => r.DecidedBy == id && In(r.DecidedAt)),
                mine.Count, minutes.Count == 0 ? null : Math.Round(minutes.Average(), 0)];
        }).ToList();
        return Table(d, ReportTypes.Staff, "Отчёт по сотрудникам",
            [new("Сотрудников", rows.Count, "int"), new("Принято платежей", rows.Sum(r => (decimal)r[7]!), "money"), new("Уборок", cleanings.Count, "int")],
            [Col("name", "Сотрудник"), Col("role", "Роль"), Col("bookings", "Создал броней", "int"), Col("checkIns", "Заселил", "int"), Col("checkOuts", "Выселил", "int"),
                Col("cancellations", "Отменил", "int"), Col("payments", "Платежей", "int"), Col("paymentsSum", "Принял, сом", "money"),
                Col("refunds", "Решений по возвратам", "int"), Col("cleanings", "Уборок", "int"), Col("cleaningMinutes", "Уборка, мин (сред.)", "int")],
            rows,
            ["Итого", null, rows.Sum(r => (int)r[2]!), rows.Sum(r => (int)r[3]!), rows.Sum(r => (int)r[4]!), rows.Sum(r => (int)r[5]!),
                rows.Sum(r => (int)r[6]!), rows.Sum(r => (decimal)r[7]!), rows.Sum(r => (int)r[8]!), rows.Sum(r => (int)r[9]!), null],
            "Сотрудник, создавший бронь с сайта, — сам гость; такие брони здесь не считаются.");
    }

    private async Task<Dictionary<Guid, string>> NumbersAsync(IEnumerable<Guid?> bookingIds, PeriodData d, CancellationToken ct)
    {
        var known = d.Bookings.ToDictionary(b => b.BookingId, b => b.Number);
        var missing = bookingIds.OfType<Guid>().Distinct().Where(id => !known.ContainsKey(id)).ToList();
        if (missing.Count > 0)
        {
            foreach (var c in await charges.ListAsync(missing, ct))
            {
                known[c.BookingId] = c.Number;
            }
        }

        return known;
    }

    private async Task<ReportTable> PaymentsAsync(PeriodData d, Guid? staffId, CancellationToken ct)
    {
        var list = d.Payments.Where(p => staffId is null || p.RegisteredBy == staffId).ToList();
        var numbers = await NumbersAsync(list.Select(p => p.BookingId), d, ct);
        var names = await users.GetNamesAsync(list.Select(p => p.RegisteredBy).OfType<Guid>().Distinct(), ct);
        var rows = list.Select(p => (IReadOnlyList<object?>)[
            data.Format(p.PaidAt), p.BookingId is { } b ? numbers.GetValueOrDefault(b) : null, Labels.PaymentType(p.Type), Labels.Method(p.Method), p.Amount,
            Labels.PaymentStatus(p.Status), p.RegisteredBy is { } r ? names.GetValueOrDefault(r) : null]).ToList();
        var summary = new List<ReportSummaryItem> { new("Поступления", list.Sum(p => p.Amount), "money", "Платежи минус аннулирования"), new("Платежей", list.Count(p => p.Type != "Reversal"), "int") };
        summary.AddRange(list.GroupBy(p => p.Method).OrderBy(g => g.Key).Select(g => new ReportSummaryItem(Labels.Method(g.Key), g.Sum(p => p.Amount), "money")));
        return Table(d, ReportTypes.Payments, "Отчёт по платежам", summary,
            [Col("paidAt", "Дата", "datetime"), Col("booking", "Бронь"), Col("type", "Назначение"), Col("method", "Способ"), Col("amount", "Сумма", "money"),
                Col("status", "Статус"), Col("registeredBy", "Принял")],
            rows,
            ["Итого", null, null, null, list.Sum(p => p.Amount), null, null]);
    }

    private async Task<ReportTable> RefundsAsync(PeriodData d, Guid? staffId, CancellationToken ct)
    {
        var list = d.Refunds.Where(r => staffId is null || r.RequestedBy == staffId || r.DecidedBy == staffId || r.PaidBy == staffId).ToList();
        var numbers = await NumbersAsync(list.Select(r => r.BookingId), d, ct);
        var names = await users.GetNamesAsync(list.SelectMany(r => new[] { r.RequestedBy, r.DecidedBy }).OfType<Guid>().Distinct(), ct);
        var rows = list.Select(r => (IReadOnlyList<object?>)[
            data.Format(r.RequestedAt), r.BookingId is { } b ? numbers.GetValueOrDefault(b) : null, r.Amount, Labels.Method(r.Method), Labels.RefundStatus(r.Status), r.Reason,
            r.RequestedBy is { } q ? names.GetValueOrDefault(q) ?? "Гость" : "Гость", r.DecidedAt is { } da ? data.Format(da) : null,
            r.DecidedBy is { } db ? names.GetValueOrDefault(db) : null, r.PaidAt is { } pa ? data.Format(pa) : null]).ToList();
        var paid = list.Where(r => r.Status == "Paid" && r.PaidAt >= d.Start && r.PaidAt < d.End).ToList();
        return Table(d, ReportTypes.Refunds, "Отчёт по возвратам",
            [
                new("Выплачено", paid.Sum(r => r.Amount), "money"),
                new("Ждут решения", list.Where(r => r.Status == "Requested").Sum(r => r.Amount), "money"),
                new("Одобрены, не выплачены", list.Where(r => r.Status == "Approved").Sum(r => r.Amount), "money"),
                new("Отклонено", list.Count(r => r.Status == "Rejected"), "int"),
            ],
            [Col("requestedAt", "Запрошен", "datetime"), Col("booking", "Бронь"), Col("amount", "Сумма", "money"), Col("method", "Способ"), Col("status", "Статус"),
                Col("reason", "Причина"), Col("requestedBy", "Запросил"), Col("decidedAt", "Решение", "datetime"), Col("decidedBy", "Решил"), Col("paidAt", "Выплачен", "datetime")],
            rows,
            ["Итого", null, list.Sum(r => r.Amount), null, null, null, null, null, null, null]);
    }

    private async Task<ReportTable> ExpensesAsync(PeriodData d, Guid? staffId, CancellationToken ct)
    {
        var list = d.Expenses.Where(e => staffId is null || e.CreatedBy == staffId).ToList();
        var names = await users.GetNamesAsync(list.Select(e => e.CreatedBy).OfType<Guid>().Distinct(), ct);
        var rows = list.Select(e => (IReadOnlyList<object?>)[
            ReportData.Format(e.Date), e.CategoryName, e.Description, e.Supplier, Labels.Method(e.Method), e.Amount, e.CreatedBy is { } c ? names.GetValueOrDefault(c) : null]).ToList();
        var summary = new List<ReportSummaryItem> { new("Расходы", list.Sum(e => e.Amount), "money") };
        summary.AddRange(list.GroupBy(e => e.CategoryName).OrderByDescending(g => g.Sum(e => e.Amount)).Select(g => new ReportSummaryItem(g.Key, g.Sum(e => e.Amount), "money")));
        return Table(d, ReportTypes.Expenses, "Отчёт по расходам", summary,
            [Col("date", "Дата", "date"), Col("category", "Категория"), Col("description", "Описание"), Col("supplier", "Поставщик"), Col("method", "Способ"),
                Col("amount", "Сумма", "money"), Col("createdBy", "Внёс")],
            rows,
            ["Итого", null, null, null, null, list.Sum(e => e.Amount), null],
            d.ExpensesApply ? null : "С фильтром по категории номера расходы не показываются: они не делятся по категориям.");
    }
}
