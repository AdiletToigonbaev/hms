using Hms.Api.Infrastructure;
using Hms.Persistence;
using Microsoft.EntityFrameworkCore;
using Serilog;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console(formatProvider: System.Globalization.CultureInfo.InvariantCulture)
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

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

    builder.Services.AddPlatform(builder.Configuration["Hotel:TimeZone"] ?? Hms.Platform.Time.HotelClock.DefaultTimeZoneId);
    builder.Services.AddPersistence(connectionString);
    builder.Services.AddHmsProblemDetails();
    builder.Services.AddHmsHealthChecks(connectionString);
    builder.Services.AddOpenApi("v1");

    var app = builder.Build();

    // Fail fast: часовой пояс отеля должен разрешаться (в образе нужен tzdata).
    var clock = app.Services.GetRequiredService<Hms.Platform.Time.IHotelClock>();
    Log.Information("Часовой пояс отеля {TimeZone}, локальное время {HotelTime:yyyy-MM-dd HH:mm}", clock.TimeZone.Id, clock.LocalNow);

    // Миграции применяются отдельной командой при релизе: `dotnet Hms.Api.dll migrate`.
    if (args.Contains("migrate", StringComparer.OrdinalIgnoreCase))
    {
        await using var scope = app.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<HmsDbContext>().Database.MigrateAsync();
        Log.Information("Миграции БД применены");
        return 0;
    }

    app.UseMiddleware<CorrelationIdMiddleware>();
    app.UseSerilogRequestLogging(options =>
        options.EnrichDiagnosticContext = (diagnostics, http) =>
            diagnostics.Set("CorrelationId", CorrelationIdMiddleware.Get(http)));
    app.UseExceptionHandler();
    app.UseStatusCodePages();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi("/openapi/{documentName}.json");
    }

    app.MapHmsHealthChecks();

    // Бизнес-эндпоинты модулей подключаются к этой группе начиная со Sprint 1.
    app.MapGroup("/api/v1");

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
