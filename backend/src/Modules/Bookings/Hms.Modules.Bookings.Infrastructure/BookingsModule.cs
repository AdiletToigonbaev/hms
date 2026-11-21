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

    public static IServiceCollection AddBookingsModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, BookingsModelConfigurator>();
        services.AddScoped<IRoomBookingQueries, RoomBookingQueries>();
        services.AddScoped<IGuestBookingQueries, GuestBookingQueries>();
        services.AddScoped<Stays>();
        services.AddScoped<IAvailabilityService, AvailabilityService>();
        services.AddScoped<IBookingService, BookingService>();
        services.AddScoped<ICalendarService, CalendarService>();
        services.AddScoped<IBookingLifecycle, BookingLifecycle>();
        services.AddOptions<BookingJobsOptions>().BindConfiguration(BookingJobsOptions.Section);
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
        g.MapPost("/{id:guid}/reschedule/quote", async (Guid id, RescheduleBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.QuoteRescheduleAsync(id, r, ct)).ToHttpResult())
            .WithValidation<RescheduleBookingRequest>().RequirePermission(Permissions.BookingEdit);
        g.MapPost("/{id:guid}/reschedule", async (Guid id, RescheduleBookingRequest r, IBookingService s, CancellationToken ct) =>
                (await s.RescheduleAsync(id, r, ct)).ToHttpResult())
            .WithValidation<RescheduleBookingRequest>().RequirePermission(Permissions.BookingEdit);

        api.MapGet("/calendar", async ([AsParameters] CalendarQuery q, ICalendarService s, CancellationToken ct) => (await s.GetAsync(q, ct)).ToHttpResult())
            .WithTags("Bookings").RequirePermission(Permissions.CalendarView);
        return api;
    }

    private static IResult Invalid(FluentValidation.Results.ValidationResult check) =>
        ResultHttpExtensions.ValidationProblem(check.Errors
            .GroupBy(e => char.ToLowerInvariant(e.PropertyName[0]) + e.PropertyName[1..])
            .ToDictionary(x => x.Key, x => x.Select(e => e.ErrorMessage).Distinct().ToArray()));
}
