using System.Security.Claims;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.Platform.Security;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Hms.Modules.Identity.Infrastructure.Services;

/// <summary>Выпуск пары токенов: JWT access (15 мин) + refresh (14 дней, хранится хэш).</summary>
internal sealed class TokenIssuer(
    DbContext db,
    IUserAccessProvider access,
    ICurrentUser currentUser,
    TimeProvider time,
    IOptions<JwtOptions> jwtOptions)
{
    private readonly JwtOptions _jwt = jwtOptions.Value;

    /// <summary>
    /// Создаёт пару токенов. Refresh-токен добавляется в контекст; сохраняет вызывающий код
    /// (вместе с остальными изменениями операции).
    /// </summary>
    public async Task<TokenResponse> IssueAsync(HmsUser user, Guid? familyId, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var state = await access.GetAsync(user.Id, ct)
            ?? throw new InvalidOperationException("Пользователь не найден при выпуске токена.");

        var claims = new List<Claim>
        {
            new(HmsClaimTypes.Subject, user.Id.ToString()),
            new(HmsClaimTypes.Name, user.FullName),
            new(HmsClaimTypes.UserType, user.UserType.ToString()),
            new(HmsClaimTypes.SecurityStamp, user.SecurityStamp ?? string.Empty),
            new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString("N")),
        };
        if (!string.IsNullOrEmpty(user.Email))
        {
            claims.Add(new Claim(HmsClaimTypes.Email, user.Email));
        }

        claims.AddRange(state.Roles.Select(r => new Claim(HmsClaimTypes.Role, r)));

        var accessExpires = now + _jwt.AccessTokenLifetime;
        var accessToken = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            Subject = new ClaimsIdentity(claims),
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = accessExpires.UtcDateTime,
            SigningCredentials = new SigningCredentials(new SymmetricSecurityKey(_jwt.SigningKeyBytes()), SecurityAlgorithms.HmacSha256),
        });

        var (refresh, plain) = RefreshToken.Issue(user.Id, familyId, now, _jwt.RefreshTokenLifetime, currentUser.IpAddress, currentUser.UserAgent);
        db.Set<RefreshToken>().Add(refresh);

        return new TokenResponse(accessToken, accessExpires, plain, refresh.ExpiresAt, MeMapper.ToMe(user, state));
    }
}

internal static class MeMapper
{
    public static MeResponse ToMe(HmsUser user, UserAccess state) => new(
        user.Id,
        user.FullName,
        user.Email,
        user.EmailConfirmed,
        user.PhoneNumber,
        user.UserType.ToString(),
        state.Roles.Order(StringComparer.Ordinal).ToList(),
        state.Permissions.Order(StringComparer.Ordinal).ToList(),
        user.MustChangePassword);
}
