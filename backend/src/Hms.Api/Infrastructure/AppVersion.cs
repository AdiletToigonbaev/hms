namespace Hms.Api.Infrastructure;

/// <summary>
/// Версия выпуска (git-метка вида v1.2.0, при выпуске без метки — хеш коммита). Задаётся deploy/prod.sh
/// через окружение (App__Version); в разработке — «dev». Видна в /health/ready, в логе старта и в кабинете персонала.
/// </summary>
public static class AppVersion
{
    public const string Section = "App:Version";

    public static string From(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var value = configuration[Section]?.Trim();
        return string.IsNullOrEmpty(value) ? "dev" : value;
    }
}
