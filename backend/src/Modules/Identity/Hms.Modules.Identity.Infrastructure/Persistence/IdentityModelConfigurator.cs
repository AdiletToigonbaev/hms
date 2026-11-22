using Hms.Modules.Identity.Domain;
using Hms.Platform.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hms.Modules.Identity.Infrastructure.Persistence;

/// <summary>
/// Таблицы схемы <c>identity</c>. Конфигурация таблиц ASP.NET Identity повторяет IdentityDbContext
/// (общий HmsDbContext от него не наследуется), с отличиями: email и телефон уникальны на уровне БД.
/// </summary>
internal sealed class IdentityModelConfigurator : IModelConfigurator
{
    public const string Schema = "identity";

    public void Configure(ModelBuilder b)
    {
        b.Entity<HmsUser>(e =>
        {
            e.ToTable("users", Schema);
            e.HasKey(u => u.Id);
            e.Property(u => u.ConcurrencyStamp).IsConcurrencyToken();
            e.Property(u => u.UserName).HasMaxLength(256);
            e.Property(u => u.NormalizedUserName).HasMaxLength(256);
            e.Property(u => u.Email).HasMaxLength(256);
            e.Property(u => u.NormalizedEmail).HasMaxLength(256);
            e.Property(u => u.PhoneNumber).HasMaxLength(32);
            e.Property(u => u.FullName).HasMaxLength(200).IsRequired();
            e.Property(u => u.UserType).HasConversion<string>().HasMaxLength(20);
            e.HasIndex(u => u.NormalizedUserName).IsUnique();
            // Уникальность защищает от гонки двух одновременных регистраций (проверка в коде — check-then-insert).
            e.HasIndex(u => u.NormalizedEmail).IsUnique().HasFilter("normalized_email IS NOT NULL");
            e.HasIndex(u => u.PhoneNumber).IsUnique().HasFilter("phone_number IS NOT NULL");
            e.HasIndex(u => new { u.UserType, u.IsActive });

            e.HasMany<IdentityUserClaim<Guid>>().WithOne().HasForeignKey(x => x.UserId).IsRequired();
            e.HasMany<IdentityUserLogin<Guid>>().WithOne().HasForeignKey(x => x.UserId).IsRequired();
            e.HasMany<IdentityUserToken<Guid>>().WithOne().HasForeignKey(x => x.UserId).IsRequired();
            e.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(x => x.UserId).IsRequired();
        });

        b.Entity<IdentityUserClaim<Guid>>(e =>
        {
            e.ToTable("user_claims", Schema);
            e.HasKey(x => x.Id);
        });

        b.Entity<IdentityUserLogin<Guid>>(e =>
        {
            e.ToTable("user_logins", Schema);
            e.HasKey(x => new { x.LoginProvider, x.ProviderKey });
            e.Property(x => x.LoginProvider).HasMaxLength(128);
            e.Property(x => x.ProviderKey).HasMaxLength(128);
        });

        b.Entity<IdentityUserToken<Guid>>(e =>
        {
            e.ToTable("user_tokens", Schema);
            e.HasKey(x => new { x.UserId, x.LoginProvider, x.Name });
            e.Property(x => x.LoginProvider).HasMaxLength(128);
            e.Property(x => x.Name).HasMaxLength(128);
        });

        b.Entity<HmsRole>(e =>
        {
            e.ToTable("roles", Schema);
            e.HasKey(r => r.Id);
            e.Property(r => r.ConcurrencyStamp).IsConcurrencyToken();
            e.Property(r => r.Name).HasMaxLength(256);
            e.Property(r => r.NormalizedName).HasMaxLength(256);
            e.HasIndex(r => r.NormalizedName).IsUnique();
            e.HasMany<IdentityUserRole<Guid>>().WithOne().HasForeignKey(x => x.RoleId).IsRequired();
            e.HasMany<IdentityRoleClaim<Guid>>().WithOne().HasForeignKey(x => x.RoleId).IsRequired();
        });

        b.Entity<IdentityRoleClaim<Guid>>(e =>
        {
            e.ToTable("role_claims", Schema);
            e.HasKey(x => x.Id);
        });

        b.Entity<IdentityUserRole<Guid>>(e =>
        {
            e.ToTable("user_roles", Schema);
            e.HasKey(x => new { x.UserId, x.RoleId });
        });

        b.Entity<PermissionEntity>(e =>
        {
            e.ToTable("permissions", Schema);
            e.HasKey(x => x.Code);
            e.Property(x => x.Code).HasMaxLength(100);
            e.Property(x => x.Group).HasMaxLength(100).IsRequired();
            e.Property(x => x.Description).HasMaxLength(300).IsRequired();
        });

        b.Entity<RolePermissionGrant>(e =>
        {
            e.ToTable("role_permissions", Schema);
            e.HasKey(x => new { x.RoleId, x.PermissionCode });
            e.Property(x => x.PermissionCode).HasMaxLength(100);
            e.Property(x => x.LimitAmount).HasPrecision(14, 2);
            e.HasOne<HmsRole>().WithMany().HasForeignKey(x => x.RoleId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PermissionEntity>().WithMany().HasForeignKey(x => x.PermissionCode).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<PermissionOverride>(e =>
        {
            e.ToTable("user_permissions", Schema);
            e.HasKey(x => new { x.UserId, x.PermissionCode });
            e.Property(x => x.PermissionCode).HasMaxLength(100);
            e.Property(x => x.LimitAmount).HasPrecision(14, 2);
            e.HasOne<HmsUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne<PermissionEntity>().WithMany().HasForeignKey(x => x.PermissionCode).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.TokenHash).HasMaxLength(64).IsRequired();
            e.Property(x => x.RevokeReason).HasConversion<string>().HasMaxLength(30);
            e.Property(x => x.CreatedByIp).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(256);
            e.HasIndex(x => x.TokenHash).IsUnique();
            e.HasIndex(x => x.FamilyId);
            e.HasIndex(x => new { x.UserId, x.RevokedAt });
            e.HasOne<HmsUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
