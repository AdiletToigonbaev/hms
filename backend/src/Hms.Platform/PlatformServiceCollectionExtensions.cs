using FluentValidation;
using Hms.Platform.Email;
using Hms.Platform.Files;
using Hms.Platform.Outbox;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Hms.Platform.Time;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Microsoft.Extensions.DependencyInjection;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>Регистрирует общие сервисы платформы: время, текущий пользователь, outbox, email.</summary>
    public static IServiceCollection AddPlatform(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        var timeZoneId = configuration["Hotel:TimeZone"] ?? HotelClock.DefaultTimeZoneId;
        services.AddSingleton(TimeProvider.System);
        services.AddSingleton<IHotelClock>(sp => new HotelClock(sp.GetRequiredService<TimeProvider>(), timeZoneId));

        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();

        services.AddSingleton<IModelConfigurator, PlatformModelConfigurator>();

        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));
        services.AddScoped<IOutbox, Outbox>();
        services.AddScoped<IOutboxProcessor, OutboxProcessor>();
        services.AddScoped<Hms.Platform.Webhooks.IWebhookInbox, Hms.Platform.Webhooks.WebhookInbox>();
        services.AddHostedService<OutboxBackgroundService>();

        services.Configure<EmailOptions>(configuration.GetSection(EmailOptions.Section));
        services.AddScoped<IEmailOutbox, EmailOutbox>();
        services.AddScoped<IOutboxHandler, SendEmailOutboxHandler>();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<IEmailComposer, EmailComposer>();
        // Сведения о гостинице для писем — модуль Hotel регистрирует свою реализацию (последняя регистрация побеждает).
        services.TryAddScoped<IEmailBrandProvider, DefaultEmailBrandProvider>();

        services.Configure<FileStorageOptions>(configuration.GetSection(FileStorageOptions.Section));
        services.AddSingleton<IFileStorage, LocalFileStorage>();
        services.AddScoped<IFileService, FileService>();

        // Сообщения валидации — на русском
        ValidatorOptions.Global.LanguageManager.Culture = new System.Globalization.CultureInfo("ru");

        return services;
    }
}
