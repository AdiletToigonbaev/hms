namespace Hms.Platform.Security;

/// <summary>Контекст текущего запроса: кто выполняет действие и откуда (для прав, аудита, логов).</summary>
public interface ICurrentUser
{
    bool IsAuthenticated { get; }

    /// <summary>Идентификатор пользователя или null для анонимного запроса / фоновой задачи.</summary>
    Guid? UserId { get; }

    /// <summary>Email из токена (для служебных писем пользователю).</summary>
    string? Email { get; }

    string? IpAddress { get; }

    string? UserAgent { get; }

    string? CorrelationId { get; }

    bool IsInRole(string role);
}
