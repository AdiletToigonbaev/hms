using System.Security.Cryptography;
using System.Text;

namespace Hms.Modules.Identity.Domain;

public enum RefreshTokenRevokeReason
{
    Rotated,
    Logout,
    ReuseDetected,
    PasswordChanged,
    UserDeactivated,
    AdminReset,
}

/// <summary>
/// Refresh-токен. В БД хранится только SHA-256 хэш. Токены одной сессии образуют «семейство»:
/// при обновлении старый токен отзывается (Rotated) и выдаётся новый в том же семействе.
/// Повторное предъявление отозванного токена означает кражу — отзывается всё семейство.
/// </summary>
public sealed class RefreshToken
{
    /// <summary>Повтор только что заменённого токена в этом окне считается гонкой параллельных вкладок, а не кражей.</summary>
    public static readonly TimeSpan RotationGracePeriod = TimeSpan.FromSeconds(30);

    private RefreshToken()
    {
        TokenHash = string.Empty;
    }

    private RefreshToken(Guid id, Guid userId, Guid familyId, string tokenHash, DateTimeOffset createdAt, DateTimeOffset expiresAt, string? ip, string? userAgent)
    {
        Id = id;
        UserId = userId;
        FamilyId = familyId;
        TokenHash = tokenHash;
        CreatedAt = createdAt;
        ExpiresAt = expiresAt;
        CreatedByIp = ip;
        UserAgent = userAgent;
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public Guid FamilyId { get; private set; }

    public string TokenHash { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }

    public RefreshTokenRevokeReason? RevokeReason { get; private set; }

    public Guid? ReplacedByTokenId { get; private set; }

    public string? CreatedByIp { get; private set; }

    public string? UserAgent { get; private set; }

    /// <summary>Создаёт токен; возвращает сущность и открытое значение (передаётся клиенту один раз).</summary>
    public static (RefreshToken Token, string PlainValue) Issue(
        Guid userId, Guid? familyId, DateTimeOffset now, TimeSpan lifetime, string? ip, string? userAgent)
    {
        var plain = Base64Url(RandomNumberGenerator.GetBytes(32));
        var token = new RefreshToken(Guid.CreateVersion7(), userId, familyId ?? Guid.CreateVersion7(), Hash(plain), now, now + lifetime, ip, userAgent);
        return (token, plain);
    }

    public static string Hash(string plainValue)
    {
        ArgumentNullException.ThrowIfNull(plainValue);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(plainValue)));
    }

    public bool IsActive(DateTimeOffset now) => RevokedAt is null && now < ExpiresAt;

    /// <summary>Отозван ли токен ротацией только что (повтор от параллельного запроса той же сессии).</summary>
    public bool WasJustRotated(DateTimeOffset now) =>
        RevokeReason == RefreshTokenRevokeReason.Rotated && RevokedAt is { } at && now - at <= RotationGracePeriod;

    public void Revoke(DateTimeOffset now, RefreshTokenRevokeReason reason, Guid? replacedBy = null)
    {
        if (RevokedAt is not null)
        {
            return;
        }

        RevokedAt = now;
        RevokeReason = reason;
        ReplacedByTokenId = replacedBy;
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
