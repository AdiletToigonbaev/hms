using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Hms.Platform.Files;

public static class FileEndpoints
{
    /// <summary>
    /// GET /files/{id} — отдаёт публичные файлы (фото номеров). Приватные файлы отдаются эндпоинтами
    /// модулей-владельцев с проверкой прав; здесь для них — 404, чтобы не раскрывать существование.
    /// </summary>
    public static IEndpointRouteBuilder MapFileEndpoints(this IEndpointRouteBuilder api)
    {
        api.MapGet("/files/{id:guid}", async (Guid id, IFileService files, IFileStorage storage, HttpContext http, CancellationToken ct) =>
            {
                var file = await files.FindAsync(id, ct);
                if (file is not { Visibility: FileVisibility.Public })
                {
                    return Results.NotFound();
                }

                http.Response.Headers.CacheControl = "public, max-age=31536000, immutable";
                http.Response.Headers.XContentTypeOptions = "nosniff";
                http.Response.Headers.ContentSecurityPolicy = "default-src 'none'; sandbox";
                return Results.Stream(storage.OpenRead(file.Path), file.ContentType, enableRangeProcessing: true);
            })
            .WithTags("Files")
            .AllowAnonymous();
        return api;
    }
}
