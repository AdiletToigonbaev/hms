using FluentValidation;
using Hms.Modules.Bookings.Application;
using Hms.Modules.Bookings.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Bookings.Infrastructure;

public static class BookingsModule
{
    /// <summary>Политика лимита для публичного подбора номеров (настраивается в Api).</summary>
    public const string PublicSearchRateLimit = "public-search";

    /// <summary>Политика лимита для создания и продления броней гостем (на аккаунт; настраивается в Api).</summary>
    public const string GuestBookingRateLimit = "guest-booking";

    public static IServiceCollection AddBookingsModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, BookingsModelConfigurator>();
        services.AddScoped<IRoomBookingQueries, RoomBookingQueries>();
        services.AddScoped<IGuestBookingQueries, GuestBookingQueries>();
        services.AddScoped<IBookingCharges, BookingCharges>();
        services.AddScoped<Stays>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<BookingService>();
        services.AddScoped<IBookingService>(sp => sp.GetRequiredService<BookingService>());
        services.AddScoped<IGuestBookingService, GuestBookingService>();
        services.AddScoped<IBookingFacts, BookingFacts>();
        services.AddScoped<IBookingNotices, BookingNotices>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IFrontDeskService, FrontDeskService>();
        services.AddScoped<IBookingLifecycle, BookingLifecycle>();
        services.AddOptions<BookingJobsOptions>().BindConfiguration(BookingJobsOptions.Section);
        services.AddOptions<GuestBookingOptions>().BindConfiguration(GuestBookingOptions.Section);
        services.AddHostedService<BookingLifecycleBackgroundService>();
        services.AddValidatorsFromAssemblyContaining<CreateBookingRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapBookingsEndpoints(this IEndpointRouteBuilder api)
    {
        // Публичный подбор: категории и цена «от», без номеров комнат. Ограничение частоты — от перебора.
        api.MapGet("/public/availability", async ([AsParameters] AvailabilityQuery q, IAvailabilityService s, IValidator<AvailabilityQuery> v, CancellationToken ct) =>
            {
                var check = await v.ValidateAsync(q, ct);
                return check.IsValid ? (await s.FindPublicAsync(q, ct)).ToHttpResult() : Invalid(check);
            })
            .WithTags("Public").AllowAnonymous().RequireRateLimiting(PublicSearchRateLimit);
        api.MapPost("/public/bookings/quote", async (PublicQuoteRequest r, IAvailabilityService s, CancellationToken ct) =>
                (await s.QuotePublicAsync(r, ct)).ToHttpResult())
            .WithValidation<PublicQuoteRequest>().WithTags("Public").AllowAnonymous().RequireRateLimiting(PublicSearchRateLimit);

        api.MapGet("/availability", async ([AsParameters] AvailabilityQuery q, IAvailabilityService s, IValidator<AvailabilityQuery> v, CancellationToken ct) =>
            {
                var check = await v.ValidateAsync(q, ct);
                return check.IsValid ? (await s.FindRoomsAsync(q, ct)).ToHttpResult() : Invalid(check);
            })
            .WithTags("Bookings").RequirePermission(Permissions.BookingCreate);
        api.MapPost("/bookings/quote", async (QuoteRequest r, IAvailabilityService s, CancellationToken ct) => (await s.QuoteAsync(r, ct)).ToHttpResult())
            .WithValidation<QuoteRequest>().WithTags("Bookings").RequirePermission(Permissions.BookingCreate);

        var g = api.MapGroup("/bookings").WithTags("Bookings");
        g.MapGet("/", async ([AsParameters] BookingQuery q, IBookingService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequirePermission(Permissions.BookingView);
        g.MapGet("/{id:guid}", async (Guid id, IBookingService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.BookingView);
        g.MapGet("/{id:guid}/history", async (Guid id, IBookingService s, CancellationToken ct) => (await s.GetHistoryAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.BookingView);
        g.MapPost("/", async (CreateBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(b => Results.Created($"/api/v1/bookings/{b.Id}", b)))
            .WithValidation<CreateBookingRequest>()
            .WithIdempotency("booking.create")
            .RequirePermission(Permissions.BookingCreate);
        g.MapPut("/{id:guid}", async (Guid id, UpdateBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<UpdateBookingRequest>().RequirePermission(Permissions.BookingEdit);
        g.MapPost("/{id:guid}/confirm", async (Guid id, IBookingService s, CancellationToken ct) => (await s.ConfirmAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.BookingEdit);
        g.MapGet("/{id:guid}/extend/quote", async (Guid id, int units, IBookingService s, CancellationToken ct) =>
                (await s.QuoteExtensionAsync(id, units, ct)).ToHttpResult())
            .RequirePermission(Permissions.BookingEdit);
        g.MapPost("/{id:guid}/extend", async (Guid id, ExtendBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.ExtendAsync(id, r, ct)).ToHttpResult())
            .WithValidation<ExtendBookingRequest>().RequirePermission(Permissions.BookingEdit);
        g.MapGet("/{id:guid}/cancel/preview", async (Guid id, IBookingService s, CancellationToken ct) => (await s.QuoteCancellationAsync(id, ct)).ToHttpResult())
            .RequireAnyPermission(Permissions.BookingCancel, Permissions.BookingEdit);
        g.MapPost("/{id:guid}/cancel", async (Guid id, CancelBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.CancelAsync(id, r, ct)).ToHttpResult())
            .WithValidation<CancelBookingRequest>().RequirePermission(Permissions.BookingCancel);
        g.MapPost("/{id:guid}/no-show", async (Guid id, NoShowRequest r, IBookingService s, CancellationToken ct) =>
                (await s.MarkNoShowAsync(id, r, ct)).ToHttpResult())
            .RequirePermission(Permissions.BookingEdit);
        g.MapGet("/{id:guid}/reschedule/rooms", async (Guid id, [AsParameters] RescheduleRoomsQuery q, IBookingService s, CancellationToken ct) =>
                (await s.RescheduleRoomsAsync(id, q, ct)).ToHttpResult(ids => Results.Ok(new { roomIds = ids })))
            .RequirePermission(Permissions.BookingEdit);
        g.MapPost("/{id:guid}/reschedule/quote", async (Guid id, RescheduleBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.QuoteRescheduleAsync(id, r, ct)).ToHttpResult())
            .WithValidation<RescheduleBookingRequest>().RequirePermission(Permissions.BookingEdit);
        g.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.RescheduleAsync(id, r, ct)).ToHttpResult())
            .WithValidation<RescheduleBookingRequest>().RequirePermission(Permissions.BookingEdit);

        g.MapGet("/{id:guid}/check-in/preview", async (Guid id, IFrontDeskService s, CancellationToken ct) => (await s.CheckInPreviewAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.CheckInPerform);
        g.MapPost("/{id:guid}/check-in", async (Guid id, IFrontDeskService s, CancellationToken ct) => (await s.CheckInAsync(id, ct)).ToHttpResult())
            .WithIdempotency("booking.check_in").RequirePermission(Permissions.CheckInPerform);
        g.MapGet("/{id:guid}/move/options", async (Guid id, IFrontDeskService s, CancellationToken ct) => (await s.MoveOptionsAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.BookingEdit);
        g.MapPost("/{id:guid}/move", async (Guid id, MoveGuestRequest r, IFrontDeskService s, CancellationToken ct) => (await s.MoveAsync(id, r, ct)).ToHttpResult())
            .WithValidation<MoveGuestRequest>().WithIdempotency("booking.move").RequirePermission(Permissions.BookingEdit);
        g.MapGet("/{id:guid}/check-out/preview", async (Guid id, IFrontDeskService s, CancellationToken ct) => (await s.CheckOutPreviewAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.CheckOutPerform);
        g.MapPost("/{id:guid}/check-out", async (Guid id, CheckOutRequest r, IFrontDeskService s, CancellationToken ct) => (await s.CheckOutAsync(id, r, ct)).ToHttpResult())
            .WithIdempotency("booking.check_out").RequirePermission(Permissions.CheckOutPerform);

        // Счётчик для значка в меню «Бронирования»: брони, ждущие подтверждения.
        g.MapGet("/pending-count", async (IBookingFacts facts, Hms.Platform.Time.IHotelClock clock, CancellationToken ct) =>
                Results.Ok(new { pending = await facts.PendingCountAsync(clock.UtcNow, ct) }))
            .RequirePermission(Permissions.BookingView);

        // Кабинет гостя: только свои брони (проверка владения — в сервисе; чужая бронь — 404).
        var me = api.MapGroup("/me/bookings").WithTags("Account").RequireAuthorization();
        me.MapGet("/", async ([AsParameters] GuestBookingQuery q, IGuestBookingService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)));
        me.MapPost("/", async (GuestCreateBookingRequest r, IGuestBookingService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(b => Results.Created($"/api/v1/me/bookings/{b.Id}", b)))
            .WithValidation<GuestCreateBookingRequest>().WithIdempotency("me.booking.create").RequireRateLimiting(GuestBookingRateLimit);
        me.MapGet("/{id:guid}", async (Guid id, IGuestBookingService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult());
        me.MapGet("/{id:guid}/cancel/preview", async (Guid id, IGuestBookingService s, CancellationToken ct) => (await s.QuoteCancellationAsync(id, ct)).ToHttpResult());
        me.MapPost("/{id:guid}/cancel", async (Guid id, GuestCancelRequest r, IGuestBookingService s, CancellationToken ct) => (await s.CancelAsync(id, r, ct)).ToHttpResult())
            .WithValidation<GuestCancelRequest>();
        me.MapGet("/{id:guid}/extend/quote", async (Guid id, int units, IGuestBookingService s, CancellationToken ct) => (await s.QuoteExtensionAsync(id, units, ct)).ToHttpResult());
        me.MapPost("/{id:guid}/extend", async (Guid id, ExtendBookingRequest r, IGuestBookingService s, CancellationToken ct) => (await s.ExtendAsync(id, r, ct)).ToHttpResult())
            .WithValidation<ExtendBookingRequest>().WithIdempotency("me.booking.extend").RequireRateLimiting(GuestBookingRateLimit);

        api.MapGet("/calendar", async ([AsParameters] CalendarQuery q, ICalendarService s, CancellationToken ct) => (await s.GetAsync(q, ct)).ToHttpResult())
            .WithTags("Bookings").RequirePermission(Permissions.CalendarView);
        return api;
    }

    private static IResult Invalid(FluentValidation.Results.ValidationResult check) =>
        ResultHttpExtensions.ValidationProblem(check.Errors
            .GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
            .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).Distinct().ToArray()));
}
