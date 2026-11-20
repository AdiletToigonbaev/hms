using Hms.Modules.Audit.Contracts;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.Platform.Email;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hms.Modules.Identity.Infrastructure.Services;

internal sealed class AuthService(
    UserManager<HmsUser> users,
    DbContext db,
    TokenIssuer tokens,
    IUserAccessProvider access,
    IEmailOutbox emails,
    IAuditTrail audit,
    TimeProvider time,
    IOptions<IdentityModuleOptions> options) : IAuthService
{
    public const string PasswordResetTokenProvider = "PasswordReset";

    // Хэш-пустышка: проверка пароля для несуществующего пользователя занимает столько же времени,
    // что и для существующего — по времени ответа нельзя определить, есть ли аккаунт.
    private static readonly Lazy<string> DummyHash = new(() => new PasswordHasher<HmsUser>().HashPassword(new HmsUser(), Guid.NewGuid().ToString()));

    private readonly IdentityModuleOptions _options = options.Value;

    public async Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest request, CancellationToken ct)
    {
        var email = IdentityHelpers.NormalizeEmail(request.Email);
        var phone = IdentityHelpers.NormalizePhone(request.Phone);

        if (await users.FindByEmailAsync(email) is not null)
        {
            return IdentityErrors.EmailTaken;
        }

        if (phone is not null && await IdentityHelpers.PhoneTakenAsync(db, phone, null, ct))
        {
            return IdentityErrors.PhoneTaken;
        }

        var now = time.GetUtcNow();
        var user = new HmsUser
        {
            Id = Guid.CreateVersion7(),
            FullName = request.FullName.Trim(),
            Email = email,
            PhoneNumber = phone,
            UserType = UserType.Guest,
            CreatedAt = now,
        };
        user.UserName = user.Id.ToString("N");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var created = await users.CreateAsync(user, request.Password);
            if (!created.Succeeded)
            {
                return IdentityHelpers.ToError(created);
            }

            await users.AddToRoleAsync(user, Roles.Guest);

            // Согласия фиксируются в аудите с датой, IP и источником; в Sprint 4 переносятся в GuestConsents.
            audit.Record(new AuditEntry(
                "auth.registered",
                "User",
                user.Id.ToString(),
                After: new { consents = new { personalData = request.AcceptPersonalData, whatsApp = request.AcceptWhatsApp, source = "portal.registration", at = now } },
                ActorUserId: user.Id));

            await EnqueueConfirmationAsync(user);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IdentityHelpers.FromUniqueViolation(ex) is { } error)
        {
            return error;
        }

        return new RegisterResponse(user.Id, _options.RequireConfirmedEmailForGuests);
    }

    public async Task<Result<TokenResponse>> LoginAsync(LoginRequest request, CancellationToken ct)
    {
        var user = await IdentityHelpers.FindByLoginAsync(users, db, request.Login, ct);
        if (user is null)
        {
            new PasswordHasher<HmsUser>().VerifyHashedPassword(new HmsUser(), DummyHash.Value, request.Password);
            await audit.RecordNowAsync(new AuditEntry("auth.login.failed", After: new { login = IdentityHelpers.MaskLogin(request.Login), reason = "unknown_account" }), ct);
            return IdentityErrors.InvalidCredentials;
        }

        if (!user.IsActive)
        {
            await RecordFailureAsync(user, "deactivated", ct);
            return IdentityErrors.InvalidCredentials;
        }

        if (await users.IsLockedOutAsync(user))
        {
            await RecordFailureAsync(user, "locked_out", ct);
            return IdentityErrors.LockedOut;
        }

        if (!await users.CheckPasswordAsync(user, request.Password))
        {
            await users.AccessFailedAsync(user);
            if (await users.IsLockedOutAsync(user))
            {
                await RecordFailureAsync(user, "locked_out_now", ct);
                return IdentityErrors.LockedOut;
            }

            await RecordFailureAsync(user, "wrong_password", ct);
            return IdentityErrors.InvalidCredentials;
        }

        await users.ResetAccessFailedCountAsync(user);

        // Проверяется после пароля: иначе по ответу можно было бы узнать о существовании аккаунта.
        if (user.UserType == UserType.Guest && _options.RequireConfirmedEmailForGuests && !user.EmailConfirmed)
        {
            return IdentityErrors.EmailNotConfirmed;
        }

        user.LastLoginAt = time.GetUtcNow();
        audit.Record(new AuditEntry("auth.login.succeeded", "User", user.Id.ToString(), ActorUserId: user.Id));
        var response = await tokens.IssueAsync(user, familyId: null, ct);
        await db.SaveChangesAsync(ct);
        return response;
    }

    public async Task<Result<TokenResponse>> RefreshAsync(RefreshRequest request, CancellationToken ct)
    {
        var now = time.GetUtcNow();
        var hash = RefreshToken.Hash(request.RefreshToken);
        var current = await db.Set<RefreshToken>().AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (current is null)
        {
            return IdentityErrors.RefreshTokenInvalid;
        }

        if (current.RevokedAt is not null)
        {
            if (current.WasJustRotated(now))
            {
                return IdentityErrors.RefreshTokenRotated;
            }

            // Повтор отозванного токена — признак кражи: завершаем всю сессию.
            await IdentityHelpers.RevokeFamilyAsync(db, current.FamilyId, now, RefreshTokenRevokeReason.ReuseDetected, ct);
            await audit.RecordNowAsync(new AuditEntry("auth.refresh.reuse_detected", "User", current.UserId.ToString(), After: new { familyId = current.FamilyId }, ActorUserId: current.UserId), ct);
            return IdentityErrors.RefreshTokenInvalid;
        }

        if (!current.IsActive(now))
        {
            return IdentityErrors.RefreshTokenInvalid;
        }

        var user = await users.FindByIdAsync(current.UserId.ToString());
        if (user is null || !user.IsActive)
        {
            await IdentityHelpers.RevokeFamilyAsync(db, current.FamilyId, now, RefreshTokenRevokeReason.UserDeactivated, ct);
            return IdentityErrors.RefreshTokenInvalid;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        var response = await tokens.IssueAsync(user, current.FamilyId, ct);
        var newToken = db.ChangeTracker.Entries<RefreshToken>().Single(e => e.State == EntityState.Added).Entity;

        // Атомарный отзыв: из двух параллельных обновлений одним токеном успешным будет только одно.
        var revoked = await db.Set<RefreshToken>()
            .Where(t => t.Id == current.Id && t.RevokedAt == null)
            .ExecuteUpdateAsync(
                s => s.SetProperty(t => t.RevokedAt, now)
                    .SetProperty(t => t.RevokeReason, RefreshTokenRevokeReason.Rotated)
                    .SetProperty(t => t.ReplacedByTokenId, newToken.Id),
                ct);
        if (revoked == 0)
        {
            await transaction.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            return IdentityErrors.RefreshTokenRotated;
        }

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return response;
    }

    public async Task<Result> LogoutAsync(LogoutRequest request, CancellationToken ct)
    {
        var hash = RefreshToken.Hash(request.RefreshToken);
        var token = await db.Set<RefreshToken>().AsNoTracking().FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token is not null)
        {
            await IdentityHelpers.RevokeFamilyAsync(db, token.FamilyId, time.GetUtcNow(), RefreshTokenRevokeReason.Logout, ct);
            await audit.RecordNowAsync(new AuditEntry("auth.logout", "User", token.UserId.ToString(), ActorUserId: token.UserId), ct);
        }

        // Идемпотентно: повторный выход или неизвестный токен — тоже успех.
        return Result.Success();
    }

    public async Task<Result> ConfirmEmailAsync(ConfirmEmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is null)
        {
            return IdentityErrors.InvalidToken;
        }

        if (user.EmailConfirmed)
        {
            return Result.Success();
        }

        var result = await users.ConfirmEmailAsync(user, request.Token);
        if (!result.Succeeded)
        {
            return IdentityErrors.InvalidToken;
        }

        await audit.RecordNowAsync(new AuditEntry("auth.email_confirmed", "User", user.Id.ToString(), ActorUserId: user.Id), ct);
        return Result.Success();
    }

    public async Task ResendConfirmationAsync(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(IdentityHelpers.NormalizeEmail(request.Email));
        if (user is { IsActive: true, EmailConfirmed: false })
        {
            await EnqueueConfirmationAsync(user);
            await db.SaveChangesAsync(ct);
        }
    }

    public async Task ForgotPasswordAsync(EmailRequest request, CancellationToken ct)
    {
        var user = await users.FindByEmailAsync(IdentityHelpers.NormalizeEmail(request.Email));
        if (user is not { IsActive: true })
        {
            return;
        }

        var token = await users.GenerateUserTokenAsync(user, PasswordResetTokenProvider, UserManager<HmsUser>.ResetPasswordTokenPurpose);
        var link = BuildLink("/reset-password", user.Id, token);
        emails.Enqueue(IdentityEmails.ResetPassword(user.Email!, user.FullName, link));
        audit.Record(new AuditEntry("auth.password_reset.requested", "User", user.Id.ToString(), ActorUserId: user.Id));
        await db.SaveChangesAsync(ct);
    }

    public async Task<Result> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(request.UserId.ToString());
        if (user is not { IsActive: true })
        {
            return IdentityErrors.InvalidToken;
        }

        var result = await users.ResetPasswordAsync(user, request.Token, request.NewPassword);
        if (!result.Succeeded)
        {
            return IdentityHelpers.ToError(result);
        }

        var now = time.GetUtcNow();
        user.MustChangePassword = false;
        // Ссылка пришла на этот email — значит, адрес подтверждён.
        user.EmailConfirmed = true;
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await users.UpdateAsync(user);
        await IdentityHelpers.RevokeAllSessionsAsync(db, user.Id, now, RefreshTokenRevokeReason.PasswordChanged, ct);
        access.Invalidate(user.Id);
        await audit.RecordNowAsync(new AuditEntry("auth.password_reset.completed", "User", user.Id.ToString(), ActorUserId: user.Id), ct);
        return Result.Success();
    }

    private async Task EnqueueConfirmationAsync(HmsUser user)
    {
        var token = await users.GenerateEmailConfirmationTokenAsync(user);
        emails.Enqueue(IdentityEmails.ConfirmEmail(user.Email!, user.FullName, BuildLink("/confirm-email", user.Id, token)));
    }

    private string BuildLink(string path, Guid userId, string token) =>
        $"{_options.PublicWebUrl.TrimEnd('/')}{path}?userId={userId}&token={Uri.EscapeDataString(token)}";

    private Task RecordFailureAsync(HmsUser user, string reason, CancellationToken ct) =>
        audit.RecordNowAsync(new AuditEntry("auth.login.failed", "User", user.Id.ToString(), After: new { reason }, ActorUserId: user.Id), ct);
}
