using System.Text.Json;
using Hms.Modules.Audit.Contracts;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Audit.Infrastructure;

internal sealed class AuditModelConfigurator : IModelConfigurator
{
    public const string Schema = "audit";

    public void Configure(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditLog>(e =>
        {
            e.ToTable("audit_logs", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Action).HasMaxLength(100).IsRequired();
            e.Property(x => x.EntityType).HasMaxLength(100);
            e.Property(x => x.EntityId).HasMaxLength(100);
            e.Property(x => x.Before).HasColumnType("jsonb");
            e.Property(x => x.After).HasColumnType("jsonb");
            e.Property(x => x.Ip).HasMaxLength(64);
            e.Property(x => x.UserAgent).HasMaxLength(256);
            e.Property(x => x.CorrelationId).HasMaxLength(64);
            e.HasIndex(x => new { x.EntityType, x.EntityId });
            e.HasIndex(x => new { x.UserId, x.At });
            e.HasIndex(x => x.At);
        });
    }
}

public sealed record AuditLogDto(
    Guid Id,
    DateTimeOffset At,
    Guid? UserId,
    string Action,
    string? EntityType,
    string? EntityId,
    JsonElement? Before,
    JsonElement? After,
    string? Ip,
    string? UserAgent,
    string? CorrelationId);

public static class AuditModule
{
    public static IServiceCollection AddAuditModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, AuditModelConfigurator>();
        services.AddScoped<IAuditTrail, AuditTrail>();
        return services;
    }

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder api)
    {
        var group = api.MapGroup("/audit-logs")
            .WithTags("Audit")
            .RequirePermission(Permissions.AuditView);

        group.MapGet("/", ListAsync);
        group.MapGet("/{id:guid}", GetAsync);
        return api;
    }

    private static async Task<IResult> ListAsync(
        DbContext db,
        DateTimeOffset? from,
        DateTimeOffset? to,
        Guid? userId,
        string? action,
        string? entityType,
        string? entityId,
        int? page,
        int? pageSize,
        CancellationToken ct)
    {
        var request = new PageRequest(page, pageSize);
        var query = db.Set<AuditLog>().AsNoTracking();
        if (from is not null)
        {
            query = query.Where(x => x.At >= from);
        }

        if (to is not null)
        {
            query = query.Where(x => x.At < to);
        }

        if (userId is not null)
        {
            query = query.Where(x => x.UserId == userId);
        }

        if (!string.IsNullOrWhiteSpace(action))
        {
            query = query.Where(x => x.Action.StartsWith(action));
        }

        if (!string.IsNullOrWhiteSpace(entityType))
        {
            query = query.Where(x => x.EntityType == entityType);
        }

        if (!string.IsNullOrWhiteSpace(entityId))
        {
            query = query.Where(x => x.EntityId == entityId);
        }

        var total = await query.CountAsync(ct);
        var items = await query
            .OrderByDescending(x => x.At)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .ToListAsync(ct);

        return Results.Ok(new PagedResult<AuditLogDto>(items.Select(ToDto).ToList(), request.Page, request.PageSize, total));
    }

    private static async Task<IResult> GetAsync(Guid id, DbContext db, CancellationToken ct)
    {
        var log = await db.Set<AuditLog>().AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        return log is null
            ? Error.NotFound("audit.not_found", "Запись аудита не найдена.").ToProblem()
            : Results.Ok(ToDto(log));
    }

    private static AuditLogDto ToDto(AuditLog x) => new(
        x.Id, x.At, x.UserId, x.Action, x.EntityType, x.EntityId,
        Parse(x.Before), Parse(x.After), x.Ip, x.UserAgent, x.CorrelationId);

    private static JsonElement? Parse(string? json) =>
        json is null ? null : JsonDocument.Parse(json).RootElement.Clone();
}
