using FluentValidation;
using Hms.Modules.Identity.Contracts;
using Hms.Modules.Rooms.Application;
using Hms.Platform.Http;
using Hms.Platform.Persistence;
using Hms.Platform.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Hms.Modules.Rooms.Infrastructure;

public static class RoomsModule
{
    /// <summary>Максимальный размер multipart-запроса с фото (лимит файла — 10 МБ, остальное — заголовки формы).</summary>
    private const long PhotoRequestLimit = 11 * 1024 * 1024;

    public static IServiceCollection AddRoomsModule(this IServiceCollection services)
    {
        services.AddSingleton<IModelConfigurator, RoomsModelConfigurator>();
        services.AddScoped<IDataSeeder, AmenitySeeder>();
        services.AddScoped<RoomsStore>();
        services.AddScoped<IAmenityService, AmenityService>();
        services.AddScoped<IRoomTypeService, RoomTypeService>();
        services.AddScoped<IRoomService, RoomService>();
        services.AddScoped<IRoomCatalog, RoomCatalog>();
        services.AddScoped<Hms.Modules.Rooms.Contracts.IRoomDirectory, RoomDirectory>();
        services.AddScoped<Hms.Modules.Rooms.Contracts.IRoomStatusChanger, RoomStatusChanger>();
        services.AddValidatorsFromAssemblyContaining<SaveRoomRequestValidator>(includeInternalTypes: true);
        return services;
    }

    public static IEndpointRouteBuilder MapRoomsEndpoints(this IEndpointRouteBuilder api)
    {
        // Чтение — всем, кто работает с номерами (администратор, уборка, бронирования); изменение — room.manage.
        string[] read = [Permissions.RoomManage, Permissions.RoomStatusChange, Permissions.BookingView, Permissions.HousekeepingManage];

        MapPublic(api);
        MapAmenities(api.MapGroup("/amenities").WithTags("Rooms"), read);
        MapRoomTypes(api.MapGroup("/room-types").WithTags("Rooms"), read);
        MapRooms(api.MapGroup("/rooms").WithTags("Rooms"), read);
        return api;
    }

    private static void MapPublic(IEndpointRouteBuilder api)
    {
        var pub = api.MapGroup("/public/room-types").WithTags("Public").AllowAnonymous();
        pub.MapGet("/", async (IRoomCatalog c, CancellationToken ct) => Results.Ok(await c.ListAsync(ct)));
        pub.MapGet("/{id:guid}", async (Guid id, IRoomCatalog c, CancellationToken ct) => (await c.GetAsync(id, ct)).ToHttpResult());
    }

    private static void MapAmenities(RouteGroupBuilder g, string[] read)
    {
        g.MapGet("/", async (IAmenityService s, CancellationToken ct) => Results.Ok(await s.ListAsync(ct))).RequireAnyPermission(read);
        g.MapPost("/", async (CreateAmenityRequest r, IAmenityService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(a => Results.Created($"/api/v1/amenities/{a.Id}", a)))
            .WithValidation<CreateAmenityRequest>().RequirePermission(Permissions.RoomManage);
        g.MapPut("/{id:guid}", async (Guid id, UpdateAmenityRequest r, IAmenityService s, CancellationToken ct) =>
                (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<UpdateAmenityRequest>().RequirePermission(Permissions.RoomManage);
        g.MapDelete("/{id:guid}", async (Guid id, IAmenityService s, CancellationToken ct) => (await s.DeleteAsync(id, ct)).ToHttpResult())
            .RequirePermission(Permissions.RoomManage);
    }

    private static void MapRoomTypes(RouteGroupBuilder g, string[] read)
    {
        g.MapGet("/", async (bool? includeArchived, IRoomTypeService s, CancellationToken ct) =>
            Results.Ok(await s.ListAsync(includeArchived == true, ct))).RequireAnyPermission(read);
        g.MapGet("/{id:guid}", async (Guid id, IRoomTypeService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequireAnyPermission(read);

        var manage = g.MapGroup(string.Empty).RequirePermission(Permissions.RoomManage);
        manage.MapPost("/", async (SaveRoomTypeRequest r, IRoomTypeService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(t => Results.Created($"/api/v1/room-types/{t.Id}", t)))
            .WithValidation<SaveRoomTypeRequest>();
        manage.MapPut("/{id:guid}", async (Guid id, SaveRoomTypeRequest r, IRoomTypeService s, CancellationToken ct) =>
                (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveRoomTypeRequest>();
        manage.MapPost("/{id:guid}/archive", async (Guid id, IRoomTypeService s, CancellationToken ct) => (await s.SetArchivedAsync(id, true, ct)).ToHttpResult());
        manage.MapPost("/{id:guid}/restore", async (Guid id, IRoomTypeService s, CancellationToken ct) => (await s.SetArchivedAsync(id, false, ct)).ToHttpResult());
        manage.MapPut("/{id:guid}/amenities", async (Guid id, SetAmenitiesRequest r, IRoomTypeService s, CancellationToken ct) =>
                (await s.SetAmenitiesAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SetAmenitiesRequest>();
        manage.MapPost("/{id:guid}/photos", async (Guid id, IFormFile file, IRoomTypeService s, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await s.AddPhotoAsync(id, stream, file.FileName, ct)).ToHttpResult(p => Results.Created(p.Url, p));
            })
            .DisableAntiforgery() // API работает по Bearer; CSRF к нему неприменим (security-architecture §4)
            .WithMetadata(new RequestSizeLimitMetadata(PhotoRequestLimit));
        manage.MapDelete("/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, IRoomTypeService s, CancellationToken ct) =>
            (await s.DeletePhotoAsync(id, photoId, ct)).ToHttpResult());
        manage.MapPut("/{id:guid}/photos/order", async (Guid id, ReorderPhotosRequest r, IRoomTypeService s, CancellationToken ct) =>
                (await s.ReorderPhotosAsync(id, r, ct)).ToHttpResult())
            .WithValidation<ReorderPhotosRequest>();
    }

    private static void MapRooms(RouteGroupBuilder g, string[] read)
    {
        g.MapGet("/", async ([AsParameters] RoomQuery q, IRoomService s, CancellationToken ct) => Results.Ok(await s.ListAsync(q, ct)))
            .RequireAnyPermission(read);
        g.MapGet("/board", async (IRoomService s, CancellationToken ct) =>
                Results.Ok(await s.ListAsync(new RoomQuery(null, null, null, false, null), ct)))
            .RequireAnyPermission(read);
        g.MapGet("/{id:guid}", async (Guid id, IRoomService s, CancellationToken ct) => (await s.GetAsync(id, ct)).ToHttpResult())
            .RequireAnyPermission(read);
        g.MapGet("/{id:guid}/status-history", async (Guid id, IRoomService s, CancellationToken ct) => (await s.GetStatusHistoryAsync(id, ct)).ToHttpResult())
            .RequireAnyPermission(read);
        g.MapPost("/{id:guid}/status", async (Guid id, ChangeRoomStatusRequest r, IRoomService s, CancellationToken ct) =>
                (await s.ChangeStatusAsync(id, r, ct)).ToHttpResult())
            .WithValidation<ChangeRoomStatusRequest>()
            .RequirePermission(Permissions.RoomStatusChange);

        var manage = g.MapGroup(string.Empty).RequirePermission(Permissions.RoomManage);
        manage.MapPost("/", async (SaveRoomRequest r, IRoomService s, CancellationToken ct) =>
                (await s.CreateAsync(r, ct)).ToHttpResult(x => Results.Created($"/api/v1/rooms/{x.Id}", x)))
            .WithValidation<SaveRoomRequest>();
        manage.MapPut("/{id:guid}", async (Guid id, SaveRoomRequest r, IRoomService s, CancellationToken ct) =>
                (await s.UpdateAsync(id, r, ct)).ToHttpResult())
            .WithValidation<SaveRoomRequest>();
        manage.MapDelete("/{id:guid}", async (Guid id, IRoomService s, CancellationToken ct) => (await s.DeleteAsync(id, ct)).ToHttpResult());
        manage.MapPost("/{id:guid}/archive", async (Guid id, IRoomService s, CancellationToken ct) => (await s.SetArchivedAsync(id, true, ct)).ToHttpResult());
        manage.MapPost("/{id:guid}/restore", async (Guid id, IRoomService s, CancellationToken ct) => (await s.SetArchivedAsync(id, false, ct)).ToHttpResult());
        manage.MapPost("/{id:guid}/photos", async (Guid id, IFormFile file, IRoomService s, CancellationToken ct) =>
            {
                await using var stream = file.OpenReadStream();
                return (await s.AddPhotoAsync(id, stream, file.FileName, ct)).ToHttpResult(p => Results.Created(p.Url, p));
            })
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitMetadata(PhotoRequestLimit));
        manage.MapDelete("/{id:guid}/photos/{photoId:guid}", async (Guid id, Guid photoId, IRoomService s, CancellationToken ct) =>
            (await s.DeletePhotoAsync(id, photoId, ct)).ToHttpResult());
    }

    private sealed class RequestSizeLimitMetadata(long limit) : Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata
    {
        public long? MaxRequestBodySize => limit;
    }
}
