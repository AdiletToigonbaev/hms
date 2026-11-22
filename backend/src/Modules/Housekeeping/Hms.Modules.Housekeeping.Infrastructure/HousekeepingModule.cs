using FluentValidation;
using Hms.Modules.Hotel.Contracts;
using Hms.Modules.Housekeeping.Application;
using Hms.Modules.Housekeeping.Contracts;
using Hms.Modules.Housekeeping.Domain;
using Hms.Modules.Identity.Contracts;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Housekeeping.Infrastructure;

public static class HousekeepingModule
{
    public static IServiceCollection AddHousekeepingModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, HousekeepingModelConfigurator>();
        services.AddScoped<IDataSeeder, ChecklistTemplateSeeder>();
        services.AddScoped<IHousekeepingService, HousekeepingService>();
        services.AddScoped<IMaintenanceService, MaintenanceService>();
        services.AddScoped<IHousekeepingTasks, HousekeepingTasks>();
        services.AddScoped<IHousekeepingFacts, HousekeepingFacts>();
        services.AddValidatorsFromAssemblyContaining<CreateTaskRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapHousekeepingEndpoints(this IEndpointRouteBuilder api)
    {
        var g = api.MapGroup("/housekeeping").WithTags("Housekeeping");
        g.MapGet("/tasks", async ([AsParameters] TaskQuery q, IHousekeepingService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequireAnyPermission(Permissions.HousekeepingManage, Permissions.HousekeepingInspect);
        g.MapPost("/tasks", async (CreateTaskRequest r, IHousekeepingService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(t => Results.Created($"/api/v1/housekeeping/tasks/{t.Id}", t)))
            .WithValidation<CreateTaskRequest>().RequirePermission(Permissions.HousekeepingManage);
        g.MapGet("/tasks/{id:guid}", async (Guid id, IHousekeepingService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequireAnyPermission(Permissions.HousekeepingManage, Permissions.HousekeepingInspect, Permissions.HousekeepingWork);
        g.MapPut("/tasks/{id:guid}", async (Guid id, UpdateTaskRequest r, IHousekeepingService s, CancellationToken ct) => (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<UpdateTaskRequest>().RequirePermission(Permissions.HousekeepingManage);
        g.MapDelete("/tasks/{id:guid}", async (Guid id, IHousekeepingService s, CancellationToken ct) => (await s.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.HousekeepingManage);
        g.MapPost("/tasks/{id:guid}/assign", async (Guid id, AssignTaskRequest r, IHousekeepingService s, CancellationToken ct) => (await s.AssignAsync(id, r, ct)).ToHttpResult())
            .RequirePermission(Permissions.HousekeepingManage);
        g.MapPost("/tasks/{id:guid}/take", async (Guid id, IHousekeepingService s, CancellationToken ct) => (await s.TakeAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.HousekeepingWork);
        g.MapPost("/tasks/{id:guid}/start", async (Guid id, IHousekeepingService s, CancellationToken ct) => (await s.StartAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.HousekeepingWork);
        g.MapPut("/tasks/{id:guid}/checklist/{itemId:guid}", async (Guid id, Guid itemId, ChecklistItemRequest r, IHousekeepingService s, CancellationToken ct) =>
                (await s.SetItemAsync(id, itemId, r, ct)).ToHttpResult())
            .RequirePermission(Permissions.HousekeepingWork);
        g.MapPost("/tasks/{id:guid}/complete", async (Guid id, IHousekeepingService s, CancellationToken ct) => (await s.CompleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.HousekeepingWork);
        g.MapPost("/tasks/{id:guid}/inspect", async (Guid id, InspectTaskRequest r, IHousekeepingService s, CancellationToken ct) => (await s.InspectAsync(id, r, ct)).ToHttpResult())
            .WithValidation<InspectTaskRequest>().RequirePermission(Permissions.HousekeepingInspect);
        g.MapGet("/my/tasks", async (IHousekeepingService s, CancellationToken ct) => Results.Ok(await s.MyTasksAsync(ct)))
            .RequirePermission(Permissions.HousekeepingWork);
        g.MapGet("/my/history", async (IHousekeepingService s, CancellationToken ct) => Results.Ok(await s.MyHistoryAsync(ct)))
            .RequirePermission(Permissions.HousekeepingWork);
        // Значок у пункта меню «Уборка»: уборки, которые ждут проверки.
        g.MapGet("/awaiting-inspection-count", async (IHousekeepingFacts facts, CancellationToken ct) => Results.Ok(new { count = await facts.AwaitingInspectionCountAsync(ct) }))
            .RequireAnyPermission(Permissions.HousekeepingInspect, Permissions.HousekeepingManage);
        g.MapGet("/staff", async (IHousekeepingService s, CancellationToken ct) => Results.Ok(await s.StaffAsync(ct)))
            .RequirePermission(Permissions.HousekeepingManage);
        g.MapGet("/checklist-template", async (IHousekeepingService s, CancellationToken ct) => Results.Ok(await s.GetTemplateAsync(ct)))
            .RequirePermission(Permissions.HousekeepingManage);
        g.MapPut("/checklist-template", async (SaveChecklistTemplateRequest r, IHousekeepingService s, CancellationToken ct) => (await s.SaveTemplateAsync(r, ct)).ToHttpResult())
            .WithValidation<SaveChecklistTemplateRequest>().RequirePermission(Permissions.HousekeepingManage);
        g.MapGet("/my/maintenance-requests", async (IMaintenanceService s, CancellationToken ct) => Results.Ok(await s.MineAsync(ct)))
            .RequirePermission(Permissions.MaintenanceCreate);

        var m = api.MapGroup("/maintenance-requests").WithTags("Housekeeping");
        m.MapGet("/", async ([AsParameters] MaintenanceQuery q, IMaintenanceService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequirePermission(Permissions.MaintenanceManage);
        m.MapPost("/", async (CreateMaintenanceRequest r, IMaintenanceService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(x => Results.Created($"/api/v1/maintenance-requests/{x.Id}", x)))
            .WithValidation<CreateMaintenanceRequest>().WithIdempotency("maintenance.create").RequirePermission(Permissions.MaintenanceCreate);
        m.MapPut("/{id:guid}", async (Guid id, UpdateMaintenanceRequest r, IMaintenanceService s, CancellationToken ct) => (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<UpdateMaintenanceRequest>().RequirePermission(Permissions.MaintenanceManage);
        return api;
    }
}

/// <summary>Схема <c>housekeeping</c>: задачи уборки с чек-листами, шаблон чек-листа, заявки о неисправностях.</summary>
internal sealed class HousekeepingModelConfigurator : IModelConfigurator
{
    public const string Schema = "housekeeping";

    public void Configure(ModelBuilder b)
    {
        b.Entity<HousekeepingTask>(e =>
        {
            e.ToTable("tasks", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Priority).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Origin).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Notes).HasMaxLength(500);
            e.Property(x => x.RejectReason).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.Ignore(x => x.IsOpen);
            e.HasMany(x => x.Items).WithOne().HasForeignKey(x => x.TaskId).OnDelete(DeleteBehavior.Cascade);
            e.Navigation(x => x.Items).UsePropertyAccessMode(PropertyAccessMode.Field);
            e.HasQueryFilter(x => !x.IsDeleted);
            e.HasIndex(x => new { x.AssignedTo, x.Status });
            e.HasIndex(x => new { x.RoomId, x.CreatedAt });
            e.HasIndex(x => new { x.HotelId, x.Status });
            e.HasIndex(x => x.BookingId);
        });

        b.Entity<ChecklistItem>(e =>
        {
            e.ToTable("task_checklist_items", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Text).HasMaxLength(200).IsRequired();
            e.HasIndex(x => new { x.TaskId, x.Position });
        });

        b.Entity<ChecklistTemplate>(e =>
        {
            e.ToTable("checklist_templates", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => x.HotelId).IsUnique().HasFilter("is_default");
        });

        b.Entity<MaintenanceRequest>(e =>
        {
            e.ToTable("maintenance_requests", Schema);
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Severity).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Description).HasMaxLength(1000).IsRequired();
            e.Property(x => x.ResolutionNote).HasMaxLength(500);
            e.Property(x => x.Version).IsRowVersion();
            e.HasIndex(x => new { x.HotelId, x.Status, x.CreatedAt });
            e.HasIndex(x => x.ReportedBy);
        });
    }
}

/// <summary>Чек-лист уборки по умолчанию (администратор меняет пункты в разделе «Уборка»).</summary>
internal sealed class ChecklistTemplateSeeder(DbContext db, IHotelContext hotel) : IDataSeeder
{
    public static readonly string[] DefaultItems =
    [
        "Сменить постельное бельё",
        "Сменить полотенца",
        "Убрать санузел и душ",
        "Протереть поверхности и зеркала",
        "Пропылесосить и вымыть пол",
        "Вынести мусор",
        "Пополнить расходники (мыло, бумага, вода)",
        "Проверить свет, ТВ, кондиционер, пульт",
        "Проветрить номер",
    ];

    public int Order => 40;

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        var hotelId = await hotel.GetHotelIdAsync(cancellationToken);
        if (!await db.Set<ChecklistTemplate>().AnyAsync(t => t.HotelId == hotelId && t.IsDefault, cancellationToken))
        {
            db.Set<ChecklistTemplate>().Add(new ChecklistTemplate(hotelId, "Стандартная уборка", DefaultItems));
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
