using Hms.Modules.Audit.Contracts;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Bookings.Domain;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Messaging.Contracts;
using Hms.Platform.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Hms.Modules.Bookings.Infrastructure;

/// <summary>
/// Истечение неподтверждённых броней и автоматическая неявка. Каждая бронь сохраняется отдельно:
/// если её в тот же момент изменил сотрудник (подтвердил, заселил), конфликт версий — бронь пропускается.
/// Действие записывается в историю брони и аудит без автора (система).
/// </summary>
internal sealed partial class BookingLifecycle(
    DbContext db,
    IHotelContext hotel,
    IHotelClock clock,
    IAuditTrail audit,
    IGuestNotifications notifications,
    ILogger<BookingLifecycle> logger) : IBookingLifecycle
{
    private const int BatchSize = 100;

    public async Task<LifecycleRun> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var hotelId = await hotel.GetHotelIdAsync(ct);
        var settings = await hotel.GetSettingsAsync(ct);

        var expiredIds = await db.Set<Booking>().AsNoTracking()
            .Where(b => b.HotelId == hotelId
                && (b.Status == BookingStatus.Pending || b.Status == BookingStatus.AwaitingPayment)
                && b.HoldUntil != null && b.HoldUntil <= now)
            .OrderBy(b => b.HoldUntil)
            .Select(b => b.Id)
            .Take(BatchSize)
            .ToListAsync(ct);

        var expired = 0;
        foreach (var id in expiredIds)
        {
            if (await ProcessAsync(id, b => b.Expire(now).IsSuccess, b => new AuditEntry("booking.expired", "Booking", b.Id.ToString(), After: new { b.Number }), ct, NotificationEvent.BookingCancelled))
            {
                expired++;
            }
        }

        var noShows = 0;
        if (settings.AutoNoShowEnabled)
        {
            // Кандидаты — подтверждённые брони, которые уже начались; точный момент неявки зависит от вида брони.
            var candidates = await db.Set<Booking>().AsNoTracking()
                .Where(b => b.HotelId == hotelId && b.Status == BookingStatus.Confirmed && b.StartsAt <= now)
                .OrderBy(b => b.StartsAt)
                .Select(b => new { b.Id, b.Kind, b.StartsAt, b.CheckIn })
                .Take(BatchSize * 5)
                .ToListAsync(ct);

            foreach (var c in candidates.Where(c => now >= BookingRules.NoShowAt(settings, clock, c.Kind, c.StartsAt, c.CheckIn)).Take(BatchSize))
            {
                var done = await ProcessAsync(
                    c.Id,
                    b => b.MarkNoShow(BookingPenalties.ForNoShow(b, BookingRules.Penalties(settings, b.Kind)), false, null, now).IsSuccess,
                    b => new AuditEntry("booking.no_show", "Booking", b.Id.ToString(), After: new { b.Number, b.PenaltyAmount, Auto = true }),
                    ct);
                if (done)
                {
                    noShows++;
                }
            }
        }

        if (expired + noShows > 0)
        {
            LogRun(logger, expired, noShows);
        }

        return new LifecycleRun(expired, noShows);
    }

    private async Task<bool> ProcessAsync(Guid id, Func<Booking, bool> change, Func<Booking, AuditEntry> entry, CancellationToken ct, NotificationEvent? notify = null)
    {
        var booking = await db.Set<Booking>().Include(b => b.Rooms).Include(b => b.Extensions).FirstOrDefaultAsync(b => b.Id == id, ct);
        if (booking is null || !change(booking))
        {
            db.ChangeTracker.Clear();
            return false;
        }

        audit.Record(entry(booking));
        // Истёкшая неподтверждённая бронь: гостю сообщаем, что бронь снята (текст — шаблон «Бронь отменена»).
        if (notify is { } e && booking.GuestId is { } guestId)
        {
            notifications.Enqueue(new GuestNotification(e, guestId, booking.Id));
        }

        try
        {
            await db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            // Бронь только что изменил сотрудник — его действие важнее, повторим на следующем проходе при необходимости.
            return false;
        }
        finally
        {
            db.ChangeTracker.Clear();
        }
    }

    [LoggerMessage(Level = LogLevel.Information, Message = "Брони: истекло {Expired}, неявок {NoShows}")]
    private static partial void LogRun(ILogger logger, int expired, int noShows);
}

public sealed class BookingJobsOptions
{
    public const string Section = "BookingJobs";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>Раз в минуту применяет правила истечения и неявки.</summary>
internal sealed partial class BookingLifecycleBackgroundService(
    IServiceScopeFactory scopes,
    IOptions<BookingJobsOptions> options,
    TimeProvider time,
    ILogger<BookingLifecycleBackgroundService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            return;
        }

        using var timer = new PeriodicTimer(options.Value.Interval);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IBookingLifecycle>().RunAsync(time.GetUtcNow(), stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                LogLoopError(logger, ex);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "Брони: ошибка фоновой задачи истечения и неявки")]
    private static partial void LogLoopError(ILogger logger, Exception exception);
}
