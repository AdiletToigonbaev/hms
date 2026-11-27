using Npgsql;

namespace Hms.Api.Infrastructure;

/// <summary>
/// Проверка конфигурации production при старте (API и команда migrate): значения из
/// appsettings.Development.json и deploy/docker-compose.yml (разработка) не должны попасть на сервер.
/// Ошибка — приложение не запускается; лучше упасть при релизе, чем работать с известным всем ключом.
/// </summary>
public static class ProductionConfigurationCheck
{
    private const string DevJwtKeyMarker = "dev-only";
    private static readonly string[] DevPasswords = ["owner-dev-password", "hms_dev_only"];

    public static IReadOnlyList<string> Validate(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var errors = new List<string>();

        var jwtKey = configuration["Jwt:SigningKey"] ?? string.Empty;
        if (jwtKey.Contains(DevJwtKeyMarker, StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Jwt:SigningKey — ключ разработки. Сгенерируйте новый: openssl rand -base64 48.");
        }

        var dbPassword = TryGetPassword(configuration.GetConnectionString("Hms"));
        if (dbPassword is not null && DevPasswords.Contains(dbPassword, StringComparer.Ordinal))
        {
            errors.Add("ConnectionStrings:Hms — пароль БД из окружения разработки.");
        }

        var ownerPassword = configuration["Bootstrap:Owner:Password"];
        if (ownerPassword is not null && DevPasswords.Contains(ownerPassword, StringComparer.Ordinal))
        {
            errors.Add("Bootstrap:Owner:Password — пароль владельца из окружения разработки.");
        }

        var publicUrl = configuration["Identity:PublicWebUrl"];
        if (!Uri.TryCreate(publicUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
        {
            errors.Add("Identity:PublicWebUrl должен быть адресом сайта с https:// (ссылки в письмах).");
        }

        return errors;
    }

    private static string? TryGetPassword(string? connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return null;
        }

        try
        {
            return new NpgsqlConnectionStringBuilder(connectionString).Password;
        }
        catch (ArgumentException)
        {
            return null;
        }
    }
}
