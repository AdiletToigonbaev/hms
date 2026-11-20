using Hms.Modules.Audit.Contracts;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Services;

internal sealed class AccountService(
    UserManager<HmsUser> users,
    DbContext db,
    TokenIssuer tokens,
    IUserAccessProvider access,
    IAuditTrail audit,
    TimeProvider time) : IAccountService
{
    public async Task<Result<MeResponse>> GetMeAsync(Guid userId, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        var state = await access.GetAsync(userId, ct);
        return user is null || state is null ? IdentityErrors.UserNotFound : MeMapper.ToMe(user, state);
    }

    public async Task<Result<MeResponse>> UpdateMeAsync(Guid userId, UpdateMeRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return IdentityErrors.UserNotFound;
        }

        var phone = IdentityHelpers.NormalizePhone(request.Phone);
        if (phone is not null && await IdentityHelpers.PhoneTakenAsync(db, phone, user.Id, ct))
        {
            return IdentityErrors.PhoneTaken;
        }

        var before = new { user.FullName, user.PhoneNumber };
        user.FullName = request.FullName.Trim();
        user.PhoneNumber = phone;
        audit.Record(new AuditEntry("account.updated", "User", user.Id.ToString(), before, new { user.FullName, user.PhoneNumber }));

        try
        {
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return IdentityHelpers.ToError(result);
            }
        }
        catch (DbUpdateException ex) when (IdentityHelpers.FromUniqueViolation(ex) is { } error)
        {
            return error;
        }

        access.Invalidate(user.Id);
        return await GetMeAsync(user.Id, ct);
    }

    public async Task<Result<TokenResponse>> ChangePasswordAsync(Guid userId, ChangePasswordRequest request, CancellationToken ct)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            return IdentityErrors.UserNotFound;
        }

        var result = await users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return IdentityHelpers.ToError(result);
        }

        // Смена пароля меняет SecurityStamp: старые access-токены перестают приниматься,
        // все сессии завершаются, текущему клиенту выдаётся новая пара токенов.
        user.MustChangePassword = false;
        await users.UpdateAsync(user);
        await IdentityHelpers.RevokeAllSessionsAsync(db, user.Id, time.GetUtcNow(), RefreshTokenRevokeReason.PasswordChanged, ct);
        access.Invalidate(user.Id);

        audit.Record(new AuditEntry("account.password_changed", "User", user.Id.ToString()));
        var response = await tokens.IssueAsync(user, familyId: null, ct);
        await db.SaveChangesAsync(ct);
        return response;
    }
}
