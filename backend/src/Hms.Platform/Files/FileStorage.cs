using System.Security.Cryptography;
using Hms.Platform.Security;
using Hms.SharedKernel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hms.Platform.Files;

public sealed class FileStorageOptions
{
    public const string Section = "Storage";

    /// <summary>Каталог хранилища (в Docker — volume). Вне webroot.</summary>
    public string RootPath { get; set; } = "data/files";

    /// <summary>Максимальный размер изображения, байт.</summary>
    public long MaxImageBytes { get; set; } = 10 * 1024 * 1024;
}

/// <summary>Хранилище байтов. Локальный диск сейчас; S3-совместимое — заменой реализации.</summary>
public interface IFileStorage
{
    Task WriteAsync(string path, Stream content, CancellationToken ct);

    Stream OpenRead(string path);

    void Delete(string path);
}

internal sealed class LocalFileStorage(IOptions<FileStorageOptions> options) : IFileStorage
{
    private string Root => System.IO.Path.GetFullPath(options.Value.RootPath);

    public async Task WriteAsync(string path, Stream content, CancellationToken ct)
    {
        var full = Resolve(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full)!);
        await using var file = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, useAsync: true);
        await content.CopyToAsync(file, ct);
    }

    public Stream OpenRead(string path) =>
        new FileStream(Resolve(path), FileMode.Open, FileAccess.Read, FileShare.Read, 81920, useAsync: true);

    public void Delete(string path)
    {
        var full = Resolve(path);
        if (File.Exists(full))
        {
            File.Delete(full);
        }
    }

    // Защита от выхода за пределы хранилища (path traversal), даже если путь в БД подделан.
    private string Resolve(string path)
    {
        var full = System.IO.Path.GetFullPath(System.IO.Path.Combine(Root, path));
        if (!full.StartsWith(Root + System.IO.Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new InvalidOperationException("Недопустимый путь файла.");
        }

        return full;
    }
}

/// <summary>Приём загрузок: белый список типов по сигнатуре содержимого, лимит размера, хэш.</summary>
public interface IFileService
{
    Task<Result<StoredFile>> SaveImageAsync(Stream content, string originalName, FileVisibility visibility, CancellationToken ct);

    Task<StoredFile?> FindAsync(Guid id, CancellationToken ct);
}

internal sealed class FileService(
    DbContext db,
    IFileStorage storage,
    ICurrentUser currentUser,
    TimeProvider time,
    IOptions<FileStorageOptions> options) : IFileService
{
    public static readonly Error TooLarge = Error.Validation("file.too_large", "Файл слишком большой (не более 10 МБ).");
    public static readonly Error UnsupportedType = Error.Validation("file.unsupported_type", "Поддерживаются только изображения JPEG, PNG и WebP.");
    public static readonly Error Empty = Error.Validation("file.empty", "Файл пустой.");

    public async Task<Result<StoredFile>> SaveImageAsync(Stream content, string originalName, FileVisibility visibility, CancellationToken ct)
    {
        // Читаем в память не больше лимита + 1 байт: так проверяем размер, не доверяя заголовкам.
        var max = options.Value.MaxImageBytes;
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await content.ReadAsync(chunk, ct)) > 0)
        {
            buffer.Write(chunk, 0, read);
            if (buffer.Length > max)
            {
                return TooLarge;
            }
        }

        if (buffer.Length == 0)
        {
            return Empty;
        }

        var bytes = buffer.ToArray();
        var kind = ImageSignature.Detect(bytes);
        if (kind is null)
        {
            return UnsupportedType;
        }

        var now = time.GetUtcNow();
        var id = Guid.CreateVersion7();
        var path = $"{now:yyyy}/{now:MM}/{id:N}.{kind.Value.Extension}";
        buffer.Position = 0;
        await storage.WriteAsync(path, buffer, ct);

        var file = new StoredFile(
            id,
            path,
            kind.Value.ContentType,
            bytes.LongLength,
            Convert.ToHexStringLower(SHA256.HashData(bytes)),
            SanitizeName(originalName),
            visibility,
            currentUser.UserId,
            now);
        db.Set<StoredFile>().Add(file);
        return file;
    }

    public Task<StoredFile?> FindAsync(Guid id, CancellationToken ct) =>
        db.Set<StoredFile>().AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);

    private static string SanitizeName(string name)
    {
        var fileName = System.IO.Path.GetFileName(name ?? string.Empty);
        var clean = new string(fileName.Where(c => !char.IsControl(c)).ToArray());
        return clean.Length > 200 ? clean[..200] : clean;
    }
}

/// <summary>Определение типа изображения по «магическим» байтам — расширению и Content-Type клиента не доверяем.</summary>
public static class ImageSignature
{
    public readonly record struct ImageKind(string ContentType, string Extension);

    public static ImageKind? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF)
        {
            return new ImageKind("image/jpeg", "jpg");
        }

        if (data.Length >= 8 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return new ImageKind("image/png", "png");
        }

        // RIFF....WEBP
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8) && data[8..12].SequenceEqual("WEBP"u8))
        {
            return new ImageKind("image/webp", "webp");
        }

        return null;
    }
}
