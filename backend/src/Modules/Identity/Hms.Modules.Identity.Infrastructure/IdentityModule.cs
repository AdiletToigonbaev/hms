using FluentValidation;
using Hms.Modules.Identity.Application;
using Hms.Modules.Identity.Infrastructure.Persistence;
using Hms.Modules.Identity.Infrastructure.Services;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Hms.Modules.Identity.Infrastructure;

public static class IdentityModule
{
    public static IServiceCollection AddIdentityModule(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.Section));
        services.Configure<IdentityModuleOptions>(configuration.GetSection(IdentityModuleOptions.Section));
        services.Configure<BootstrapOwnerOptions>(configuration.GetSection(BootstrapOwnerOptions.Section));

        var moduleOptions = configuration.GetSection(IdentityModuleOptions.Section).Get<IdentityModuleOptions>() ?? new IdentityModuleOptions();

        services
            .AddIdentityCore<HmsUser>(o =>
            {
                // security-architecture §1: пароль ≥ 10 символов, блокировка после 5 неудач на 15 минут
                o.Password.RequiredLength = 10;
                o.Password.RequiredUniqueChars = 4;
                o.Password.RequireDigit = false;
                o.Password.RequireLowercase = false;
                o.Password.RequireUppercase = false;
                o.Password.RequireNonAlphanumeric = false;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
                o.Lockout.AllowedForNewUsers = true;
                o.User.RequireUniqueEmail = false; // email необязателен для персонала; уникальность — индексом БД
                o.Tokens.PasswordResetTokenProvider = AuthService.PasswordResetTokenProvider;
            })
            .AddRoles<HmsRole>()
            .AddEntityFrameworkStores<DbContext>()
            .AddErrorDescriber<RussianIdentityErrorDescriber>()
            .AddDefaultTokenProviders()
            .AddTokenProvider<PasswordResetTokenProvider>(AuthService.PasswordResetTokenProvider);

        // Срок ссылки подтверждения email; у ссылки сброса пароля — свой, короче.
        services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = moduleOptions.EmailConfirmationTokenLifetime);
        services.Configure<PasswordResetTokenProviderOptions>(o =>
        {
            o.Name = AuthService.PasswordResetTokenProvider;
            o.TokenLifespan = moduleOptions.PasswordResetTokenLifetime;
        });

        services.AddMemoryCache();
        services.AddSingleton<IModelConfigurator, IdentityModelConfigurator>();
        services.AddScoped<IDataSeeder, IdentitySeeder>();
        services.AddScoped<IUserAccessProvider, UserAccessProvider>();
        services.AddScoped<TokenIssuer>();
        services.AddScoped<IAuthService, AuthService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<IStaffService, StaffService>();
        services.AddScoped<IRoleQueries, RoleQueries>();
        services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(includeInternalTypes: true);

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = new SymmetricSecurityKey(jwt.Value.SigningKeyBytes()),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                    NameClaimType = HmsClaimTypes.Name,
                    RoleClaimType = HmsClaimTypes.Role,
                };
                bearer.Events = new JwtBearerEvents { OnTokenValidated = ValidateUserStateAsync };
            });
        services.AddAuthorization();

        return services;
    }

    /// <summary>Подключает проверку «сначала смените временный пароль» (после UseAuthorization).</summary>
    public static IApplicationBuilder UsePasswordChangeEnforcement(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            if (context.User.Identity?.IsAuthenticated == true
                && context.GetEndpoint()?.Metadata.GetMetadata<AllowWhenPasswordChangeRequiredAttribute>() is null
                && Guid.TryParse(context.User.FindFirst(HmsClaimTypes.Subject)?.Value, out var userId))
            {
                var state = await context.RequestServices.GetRequiredService<IUserAccessProvider>().GetAsync(userId, context.RequestAborted);
                if (state is { MustChangePassword: true })
                {
                    var problem = Hms.SharedKernel.Error.Forbidden("auth.password_change_required", "Смените временный пароль, чтобы продолжить работу.").ToProblem();
                    await problem.ExecuteAsync(context);
                    return;
                }
            }

            await next(context);
        });

    /// <summary>
    /// Access-токен принимается, только если пользователь существует, активен и его SecurityStamp
    /// не менялся (смена пароля, блокировка) — так блокировка действует сразу, не через 15 минут.
    /// </summary>
    private static async Task ValidateUserStateAsync(TokenValidatedContext context)
    {
        var principal = context.Principal!;
        if (!Guid.TryParse(principal.FindFirst(HmsClaimTypes.Subject)?.Value, out var userId))
        {
            context.Fail("Нет идентификатора пользователя.");
            return;
        }

        var state = await context.HttpContext.RequestServices.GetRequiredService<IUserAccessProvider>()
            .GetAsync(userId, context.HttpContext.RequestAborted);
        var stamp = principal.FindFirst(HmsClaimTypes.SecurityStamp)?.Value;
        if (state is null || !state.IsActive || !string.Equals(state.SecurityStamp, stamp, StringComparison.Ordinal))
        {
            context.Fail("Сессия недействительна.");
        }
    }
}

public sealed class PasswordResetTokenProviderOptions : DataProtectionTokenProviderOptions;

/// <summary>Отдельный провайдер токенов сброса пароля — со своим (коротким) сроком жизни.</summary>
internal sealed class PasswordResetTokenProvider(
    Microsoft.AspNetCore.DataProtection.IDataProtectionProvider dataProtectionProvider,
    IOptions<PasswordResetTokenProviderOptions> options,
    Microsoft.Extensions.Logging.ILogger<DataProtectorTokenProvider<HmsUser>> logger)
    : DataProtectorTokenProvider<HmsUser>(dataProtectionProvider, options, logger);
