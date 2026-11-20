using System.Data.Common;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Services;

internal static class IdentityHelpers
{
    public static string NormalizeEmail(string email) => email.Trim().ToLowerInvariant();

    public static string? NormalizePhone(string? phone) =>
        string.IsNullOrWhiteSpace(phone) ? null : PhoneNumber.Parse(phone).Value.Value;

    /// <summary>Поиск по email или телефону (логин может быть любым из них).</summary>
    public static async Task<HmsUser?> FindByLoginAsync(UserManager<HmsUser> users, DbContext db, string login, CancellationToken ct)
    {
        var value = login.Trim();
        if (value.Contains('@', StringComparison.Ordinal))
        {
            return await users.FindByEmailAsync(value);
        }

        var phone = PhoneNumber.Parse(value);
        return phone.IsSuccess
            ? await db.Set<HmsUser>().FirstOrDefaultAsync(u => u.PhoneNumber == phone.Value.Value, ct)
            : null;
    }

    public static Task<bool> PhoneTakenAsync(DbContext db, string phone, Guid? exceptUserId, CancellationToken ct) =>
        db.Set<HmsUser>().AnyAsync(u => u.PhoneNumber == phone && u.Id != exceptUserId, ct);

    /// <summary>Отзывает все активные refresh-токены пользователя (все сессии).</summary>
    public static Task<int> RevokeAllSessionsAsync(DbContext db, Guid userId, DateTimeOffset now, RefreshTokenRevokeReason reason, CancellationToken ct) =>
        db.Set<RefreshToken>()
            .Where(t => t.UserId == userId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokeReason, reason), ct);

    public static Task<int> RevokeFamilyAsync(DbContext db, Guid familyId, DateTimeOffset now, RefreshTokenRevokeReason reason, CancellationToken ct) =>
        db.Set<RefreshToken>()
            .Where(t => t.FamilyId == familyId && t.RevokedAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now).SetProperty(t => t.RevokeReason, reason), ct);

    /// <summary>Ошибки IdentityResult → доменная ошибка (пароль, дубликаты).</summary>
    public static Error ToError(IdentityResult result)
    {
        var errors = result.Errors.ToList();
        if (errors.Any(e => e.Code == nameof(IdentityErrorDescriber.DuplicateEmail)))
        {
            return IdentityErrors.EmailTaken;
        }

        if (errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
        {
            return IdentityErrors.InvalidToken;
        }

        if (errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
        {
            return IdentityErrors.WrongCurrentPassword;
        }

        return IdentityErrors.PasswordRejected(string.Join(" ", errors.Select(e => e.Description)));
    }

    /// <summary>Нарушение уникального индекса email/телефона при гонке параллельных запросов.</summary>
    public static Error? FromUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is DbException { SqlState: "23505" } db
            ? db.Message.Contains("phone", StringComparison.OrdinalIgnoreCase) ? IdentityErrors.PhoneTaken : IdentityErrors.EmailTaken
            : null;

    /// <summary>Маскирует логин для журнала неудачных входов: <c>iv***@mail.kg</c>, <c>+996***456</c>.</summary>
    public static string MaskLogin(string login)
    {
        var value = login.Trim();
        var at = value.IndexOf('@', StringComparison.Ordinal);
        if (at > 0)
        {
            return string.Concat(value.AsSpan(0, Math.Min(2, at)), "***", value.AsSpan(at));
        }

        return value.Length <= 6 ? "***" : string.Concat(value.AsSpan(0, 4), "***", value.AsSpan(value.Length - 3));
    }
}
