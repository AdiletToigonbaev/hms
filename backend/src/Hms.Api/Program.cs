using Hms.Api.Infrastructure;
using Hms.Modules.Audit.Infrastructure;
using Hms.Modules.Bookings.Infrastructure;
using Hms.Modules.Finance.Infrastructure;
using Hms.Modules.Guests.Infrastructure;
using Hms.Modules.Hotel.Infrastructure;
using Hms.Modules.Housekeeping.Infrastructure;
using Hms.Modules.Identity.Infrastructure;
using Hms.Modules.Pricing.Infrastructure;
using Hms.Modules.Reporting.Infrastructure;
using Hms.Modules.Rooms.Infrastructure;
using Hms.Platform.Files;
using Hms.Platform.Persistence;
using Hms.Platform.Time;
using Microsoft.Extensions.Options;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    var isMigrateCommand = args.Contains("migrate", StringComparer.OrdinalIgnoreCase);

    builder.Services.AddSerilog((services, logger) => logger
        .ReadFrom.Configuration(builder.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Hms.Api"));

    var connectionString = builder.Configuration.GetConnectionString("Hms");
    if (string.IsNullOrWhiteSpace(connectionString))
    {
        throw new InvalidOperationException(
            "Не задана строка подключения ConnectionStrings:Hms (переменная окружения ConnectionStrings__Hms).");
    }

    builder.Services.AddPlatform(builder.Configuration);
    builder.Services.AddPersistence(connectionString);
    builder.Services.AddHmsProblemDetails();
    builder.Services.AddHmsHealthChecks(connectionString);
    builder.Services.AddHmsRateLimiting(builder.Configuration);
    builder.Services.AddHmsForwardedHeaders(builder.Configuration);
    builder.Services.AddOpenApi("v1");

    // Модули
    builder.Services.AddAuditModule();
    builder.Services.AddHotelModule();
    builder.Services.AddRoomsModule();
    builder.Services.AddPricingModule();
    builder.Services.AddBookingsModule();
    builder.Services.AddGuestsModule();
    builder.Services.AddFinanceModule();
    builder.Services.AddHousekeepingModule();
    builder.Services.AddReportingModule();
    builder.Services.AddIdentityModule(builder.Configuration);

    var app = builder.Build();

    // Fail fast: часовой пояс отеля должен разрешаться (в образе нужен tzdata).
    var clock = app.Services.GetRequiredService<IHotelClock>();
    Log.Information("Часовой пояс отеля {TimeZone}, локальное время {HotelTime:yyyy-MM-dd HH:mm}", clock.TimeZone.Id, clock.LocalNow);

    // Миграции и сидеры применяются отдельной командой при релизе: `dotnet Hms.Api.dll migrate`.
    if (isMigrateCommand)
    {
        await DatabaseInitializer.MigrateAndSeedAsync(app.Services);
        return 0;
    }

    // Fail fast: без ключа подписи JWT API не запускается.
    app.Services.GetRequiredService<IOptions<JwtOptions>>().Value.SigningKeyBytes();

    app.UseForwardedHeaders();
    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(options =>
        options.EnrichDiagnosticContext = (diagnostics, http) =>
            diagnostics.Set("CorrelationId", CorrelationIdMiddleware.Get(http)));
    app.UseExceptionHandler();
    app.UseStatusCodePages();
    // Лимитер — после аутентификации: политики «на аккаунт» читают пользователя из токена.
    app.UseAuthentication();
    app.UseRateLimiter();
    app.UseAuthorization();
    app.UsePasswordChangeEnforcement();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi("/openapi/{documentName}.json");
    }

    app.MapHmsHealthChecks();

    var api = app.MapGroup("/api/v1");
    api.MapIdentityEndpoints();
    api.MapAuditEndpoints();
    api.MapHotelEndpoints();
    api.MapRoomsEndpoints();
    api.MapPricingEndpoints();
    api.MapBookingsEndpoints();
    api.MapGuestsEndpoints();
    api.MapFinanceEndpoints();
    api.MapHousekeepingEndpoints();
    api.MapReportingEndpoints();
    api.MapFileEndpoints();

    await app.RunAsync();
    return 0;
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "Приложение остановлено из-за необработанного исключения");
    return 1;
}
finally
{
    await Log.CloseAndFlushAsync();
}

/// <summary>Точка входа; открыта для WebApplicationFactory в тестах.</summary>
public partial class Program;
