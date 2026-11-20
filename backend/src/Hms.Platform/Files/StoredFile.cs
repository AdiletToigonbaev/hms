namespace Hms.Platform.Files;

public enum FileVisibility
{
    /// <summary>Доступен без входа (фото номеров для публичного каталога).</summary>
    Public,

    /// <summary>Только для авторизованных (чеки, вложения расходов, документы) — проверка по сущности-владельцу.</summary>
    Private,
}

/// <summary>Загруженный файл. На диске хранится под случайным именем, вне webroot.</summary>
public sealed class StoredFile
{
    private StoredFile()
    {
        Path = string.Empty;
        ContentType = string.Empty;
        Sha256 = string.Empty;
        OriginalName = string.Empty;
    }

    internal StoredFile(Guid id, string path, string contentType, long size, string sha256, string originalName, FileVisibility visibility, Guid? uploadedBy, DateTimeOffset now)
    {
        Id = id;
        Path = path;
        ContentType = contentType;
        Size = size;
        Sha256 = sha256;
        OriginalName = originalName;
        Visibility = visibility;
        UploadedBy = uploadedBy;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    /// <summary>Относительный путь в хранилище (yyyy/MM/{id}.{ext}).</summary>
    public string Path { get; private set; }

    public string ContentType { get; private set; }

    public long Size { get; private set; }

    public string Sha256 { get; private set; }

    /// <summary>Исходное имя — только для отображения; на диск не используется.</summary>
    public string OriginalName { get; private set; }

    public FileVisibility Visibility { get; private set; }

    public Guid? UploadedBy { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
