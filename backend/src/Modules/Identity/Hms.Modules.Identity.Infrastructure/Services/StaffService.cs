using Hms.Modules.Audit.Contracts;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Identity.Domain;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Services;

internal sealed class StaffService(
    UserManager<HmsUser> users,
    DbContext db,
    IUserAccessProvider access,
    IAuditTrail audit,
    TimeProvider time) : IStaffService
{
    public async Task<PagedResult<StaffDto>> ListAsync(StaffQuery query, CancellationToken ct)
    {
        var page = new PageRequest(query.Page, query.PageSize);
        var q = StaffWithRoles();

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var pattern = $"%{query.Search.Trim().ToLowerInvariant()}%";
#pragma warning disable CA1304, CA1311 // ToLower транслируется в SQL lower(), культура .NET не участвует
            q = q.Where(x => EF.Functions.Like(x.User.FullName.ToLower(), pattern)
                || (x.User.Email != null && EF.Functions.Like(x.User.Email, pattern))
                || (x.User.PhoneNumber != null && EF.Functions.Like(x.User.PhoneNumber, pattern)));
#pragma warning restore CA1304, CA1311
        }

        if (!string.IsNullOrWhiteSpace(query.Role))
        {
            q = q.Where(x => x.Role == query.Role);
        }

        if (query.IsActive is not null)
        {
            q = q.Where(x => x.User.IsActive == query.IsActive);
        }

        var total = await q.CountAsync(ct);
        var items = await q.OrderBy(x => x.User.FullName).Skip(page.Skip).Take(page.PageSize).ToListAsync(ct);
        return new PagedResult<StaffDto>(items.Select(x => ToDto(x.User, x.Role)).ToList(), page.Page, page.PageSize, total);
    }

    public async Task<Result<StaffDto>> GetAsync(Guid id, CancellationToken ct)
    {
        var item = await StaffWithRoles().FirstOrDefaultAsync(x => x.User.Id == id, ct);
        return item is null ? IdentityErrors.StaffNotFound : ToDto(item.User, item.Role);
    }

    public async Task<Result<StaffDto>> CreateAsync(CreateStaffRequest request, CancellationToken ct)
    {
        var email = string.IsNullOrWhiteSpace(request.Email) ? null : IdentityHelpers.NormalizeEmail(request.Email);
        var phone = IdentityHelpers.NormalizePhone(request.Phone);

        if (email is not null && await users.FindByEmailAsync(email) is not null)
        {
            return IdentityErrors.EmailTaken;
        }

        if (phone is not null && await IdentityHelpers.PhoneTakenAsync(db, phone, null, ct))
        {
            return IdentityErrors.PhoneTaken;
        }

        var user = new HmsUser
        {
            Id = Guid.CreateVersion7(),
            FullName = request.FullName.Trim(),
            Email = email,
            // Адрес сотрудника вносит владелец — отдельное подтверждение не требуется.
            EmailConfirmed = email is not null,
            PhoneNumber = phone,
            UserType = UserType.Staff,
            MustChangePassword = true,
            CreatedAt = time.GetUtcNow(),
        };
        user.UserName = user.Id.ToString("N");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var created = await users.CreateAsync(user, request.InitialPassword);
            if (!created.Succeeded)
            {
                return IdentityHelpers.ToError(created);
            }

            await users.AddToRoleAsync(user, request.Role);
            audit.Record(new AuditEntry("staff.created", "User", user.Id.ToString(), After: new { user.FullName, user.Email, user.PhoneNumber, request.Role }));
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
        }
        catch (DbUpdateException ex) when (IdentityHelpers.FromUniqueViolation(ex) is { } error)
        {
            return error;
        }

        return ToDto(user, request.Role);
    }

    public async Task<Result<StaffDto>> UpdateAsync(Guid id, UpdateStaffRequest request, CancellationToken ct)
    {
        var (user, role, error) = await LoadModifiableAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var email = string.IsNullOrWhiteSpace(request.Email) ? null : IdentityHelpers.NormalizeEmail(request.Email);
        var phone = IdentityHelpers.NormalizePhone(request.Phone);
        if (email is not null && await users.FindByEmailAsync(email) is { } other && other.Id != user!.Id)
        {
            return IdentityErrors.EmailTaken;
        }

        if (phone is not null && await IdentityHelpers.PhoneTakenAsync(db, phone, user!.Id, ct))
        {
            return IdentityErrors.PhoneTaken;
        }

        var before = new { user!.FullName, user.Email, user.PhoneNumber };
        user.FullName = request.FullName.Trim();
        if (!string.Equals(user.Email, email, StringComparison.Ordinal))
        {
            await users.SetEmailAsync(user, email);
            user.EmailConfirmed = email is not null;
        }

        user.PhoneNumber = phone;
        audit.Record(new AuditEntry("staff.updated", "User", user.Id.ToString(), before, new { user.FullName, user.Email, user.PhoneNumber }));

        try
        {
            var result = await users.UpdateAsync(user);
            if (!result.Succeeded)
            {
                return IdentityHelpers.ToError(result);
            }
        }
        catch (DbUpdateException ex) when (IdentityHelpers.FromUniqueViolation(ex) is { } dup)
        {
            return dup;
        }

        access.Invalidate(user.Id);
        return ToDto(user, role!);
    }

    public async Task<Result<StaffDto>> SetActiveAsync(Guid id, bool isActive, CancellationToken ct)
    {
        var (user, role, error) = await LoadModifiableAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        if (user!.IsActive == isActive)
        {
            return ToDto(user, role!);
        }

        var now = time.GetUtcNow();
        user.IsActive = isActive;
        user.DeactivatedAt = isActive ? null : now;
        if (isActive)
        {
            await users.SetLockoutEndDateAsync(user, null);
            await users.ResetAccessFailedCountAsync(user);
        }
        else
        {
            // Новый SecurityStamp делает недействительными уже выданные access-токены.
            await users.UpdateSecurityStampAsync(user);
            await IdentityHelpers.RevokeAllSessionsAsync(db, user.Id, now, RefreshTokenRevokeReason.UserDeactivated, ct);
        }

        audit.Record(new AuditEntry(isActive ? "staff.activated" : "staff.deactivated", "User", user.Id.ToString()));
        await users.UpdateAsync(user);
        access.Invalidate(user.Id);
        return ToDto(user, role!);
    }

    public async Task<Result> ResetPasswordAsync(Guid id, StaffResetPasswordRequest request, CancellationToken ct)
    {
        var (user, _, error) = await LoadModifiableAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        var token = await users.GeneratePasswordResetTokenAsync(user!);
        var result = await users.ResetPasswordAsync(user!, token, request.NewPassword);
        if (!result.Succeeded)
        {
            return IdentityHelpers.ToError(result);
        }

        user!.MustChangePassword = true;
        await users.SetLockoutEndDateAsync(user, null);
        await users.ResetAccessFailedCountAsync(user);
        await IdentityHelpers.RevokeAllSessionsAsync(db, user.Id, time.GetUtcNow(), RefreshTokenRevokeReason.AdminReset, ct);
        audit.Record(new AuditEntry("staff.password_reset", "User", user.Id.ToString()));
        await users.UpdateAsync(user);
        access.Invalidate(user.Id);
        return Result.Success();
    }

    public async Task<Result<StaffPermissionsDto>> GetPermissionsAsync(Guid id, CancellationToken ct)
    {
        var item = await StaffWithRoles().FirstOrDefaultAsync(x => x.User.Id == id, ct);
        if (item is null)
        {
            return IdentityErrors.StaffNotFound;
        }

        return await BuildPermissionsDtoAsync(item.User.Id, item.Role, ct);
    }

    public async Task<Result<StaffPermissionsDto>> SetPermissionsAsync(Guid id, SetPermissionsRequest request, CancellationToken ct)
    {
        var (user, role, error) = await LoadModifiableAsync(id, ct);
        if (error is not null)
        {
            return error;
        }

        if (!PermissionCatalog.ConfigurableRoles.Contains(role!))
        {
            return IdentityErrors.OverridesOnlyForAdministrator;
        }

        foreach (var o in request.Overrides)
        {
            if (!PermissionCatalog.IsAllowedFor(role!, o.Code))
            {
                return IdentityErrors.PermissionNotConfigurable(o.Code);
            }
        }

        // Сохраняем только отличия от роли: «выдать» уже имеющееся и «отозвать» отсутствующее — лишние записи.
        var roleDefaults = (await RolePermissionsAsync(role!, ct)).ToHashSet(StringComparer.Ordinal);
        var normalized = request.Overrides
            .Where(o => o.IsGranted ? !roleDefaults.Contains(o.Code) || o.LimitAmount is not null : roleDefaults.Contains(o.Code))
            .Select(o => new PermissionOverride(user!.Id, o.Code, o.IsGranted, o.IsGranted ? o.LimitAmount : null))
            .ToList();

        var existing = await db.Set<PermissionOverride>().Where(o => o.UserId == user!.Id).ToListAsync(ct);
        var before = existing.Select(o => new PermissionOverrideDto(o.PermissionCode, o.IsGranted, o.LimitAmount)).ToList();
        db.Set<PermissionOverride>().RemoveRange(existing);
        db.Set<PermissionOverride>().AddRange(normalized);
        audit.Record(new AuditEntry(
            "staff.permissions.changed",
            "User",
            user!.Id.ToString(),
            before,
            normalized.Select(o => new PermissionOverrideDto(o.PermissionCode, o.IsGranted, o.LimitAmount)).ToList()));
        await db.SaveChangesAsync(ct);
        access.Invalidate(user.Id);

        return await BuildPermissionsDtoAsync(user.Id, role!, ct);
    }

    private async Task<StaffPermissionsDto> BuildPermissionsDtoAsync(Guid userId, string role, CancellationToken ct)
    {
        var rolePermissions = await RolePermissionsAsync(role, ct);
        var overrides = await db.Set<PermissionOverride>().AsNoTracking().Where(o => o.UserId == userId)
            .OrderBy(o => o.PermissionCode)
            .Select(o => new PermissionOverrideDto(o.PermissionCode, o.IsGranted, o.LimitAmount))
            .ToListAsync(ct);
        var state = await access.GetAsync(userId, ct);
        var configurable = PermissionCatalog.All.Where(p => PermissionCatalog.IsAllowedFor(role, p.Code))
            .Select(p => new PermissionDto(p.Code, p.Group, p.Description, p.OwnerOnly)).ToList();
        var roleLimits = await (
            from rp in db.Set<RolePermissionGrant>()
            join r in db.Set<HmsRole>() on rp.RoleId equals r.Id
            where r.Name == role && rp.LimitAmount != null
            select new PermissionOverrideDto(rp.PermissionCode, true, rp.LimitAmount)).AsNoTracking().ToListAsync(ct);

        return new StaffPermissionsDto(
            userId,
            role,
            rolePermissions.Order(StringComparer.Ordinal).ToList(),
            overrides,
            (state?.Permissions ?? new HashSet<string>()).Order(StringComparer.Ordinal).ToList(),
            configurable,
            roleLimits);
    }

    private Task<List<string>> RolePermissionsAsync(string role, CancellationToken ct) =>
        (from rp in db.Set<RolePermissionGrant>()
         join r in db.Set<HmsRole>() on rp.RoleId equals r.Id
         where r.Name == role
         select rp.PermissionCode).ToListAsync(ct);

    /// <summary>Сотрудник, которого можно изменять (не владелец).</summary>
    private async Task<(HmsUser? User, string? Role, Error? Error)> LoadModifiableAsync(Guid id, CancellationToken ct)
    {
        var item = await StaffWithRoles().FirstOrDefaultAsync(x => x.User.Id == id, ct);
        if (item is null)
        {
            return (null, null, IdentityErrors.StaffNotFound);
        }

        if (item.Role == Roles.Owner)
        {
            return (null, null, IdentityErrors.CannotModifyOwner);
        }

        // Повторная загрузка с отслеживанием — через UserManager (единый экземпляр сущности для его операций).
        var user = await users.FindByIdAsync(id.ToString());
        return (user, item.Role, null);
    }

    private IQueryable<StaffRow> StaffWithRoles() =>
        from u in db.Set<HmsUser>().AsNoTracking()
        join ur in db.Set<IdentityUserRole<Guid>>() on u.Id equals ur.UserId
        join r in db.Set<HmsRole>() on ur.RoleId equals r.Id
        where u.UserType == UserType.Staff
        select new StaffRow { User = u, Role = r.Name! };

    private static StaffDto ToDto(HmsUser u, string role) => new(
        u.Id, u.FullName, u.Email, u.PhoneNumber, role, Roles.DisplayName(role), u.IsActive, u.MustChangePassword, u.CreatedAt, u.LastLoginAt);

    // Инициализатор свойств (а не конструктор), чтобы EF мог дальше фильтровать и сортировать по полям.
    private sealed class StaffRow
    {
        public required HmsUser User { get; init; }

        public required string Role { get; init; }
    }
}
