using System.Text;

namespace Hms.Modules.Identity.Infrastructure;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string Issuer { get; set; } = "hms-api";

    public string Audience { get; set; } = "hms";

    /// <summary>Ключ подписи HS256, не менее 32 байт. Только из окружения/секретов.</summary>
    public string SigningKey { get; set; } = string.Empty;

    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromMinutes(15);

    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(14);

    public byte[] SigningKeyBytes()
    {
        var bytes = Encoding.UTF8.GetBytes(SigningKey);
        if (bytes.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey не задан или короче 32 байт. Задайте переменную окружения Jwt__SigningKey.");
        }

        return bytes;
    }
}

public sealed class IdentityModuleOptions
{
    public const string Section = "Identity";

    /// <summary>Публичный адрес сайта — для ссылок в письмах (подтверждение email, сброс пароля).</summary>
    public string PublicWebUrl { get; set; } = "http://localhost:3000";

    public TimeSpan EmailConfirmationTokenLifetime { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan PasswordResetTokenLifetime { get; set; } = TimeSpan.FromHours(2);
}

/// <summary>Первый владелец — создаётся командой migrate, если владельца ещё нет. Значения — из окружения.</summary>
public sealed class BootstrapOwnerOptions
{
    public const string Section = "Bootstrap:Owner";

    public string? FullName { get; set; }

    public string? Email { get; set; }

    public string? Phone { get; set; }

    public string? Password { get; set; }
}
