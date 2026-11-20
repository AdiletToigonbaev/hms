using Microsoft.AspNetCore.Http;

namespace Hms.Platform.Security;

/// <summary>Реализация <see cref="ICurrentUser"/> на основе HTTP-запроса (claims JWT: sub, role).</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    private const int MaxUserAgentLength = 256;

    private HttpContext? Http => accessor.HttpContext;

    public bool IsAuthenticated => Http?.User.Identity?.IsAuthenticated == true;

    public Guid? UserId =>
        IsAuthenticated && Guid.TryParse(Http!.User.FindFirst(HmsClaimTypes.Subject)?.Value, out var id) ? id : null;

    public string? Email => IsAuthenticated ? Http!.User.FindFirst(HmsClaimTypes.Email)?.Value : null;

    public string? IpAddress => Http?.Connection.RemoteIpAddress?.ToString();

    public string? UserAgent
    {
        get
        {
            var value = Http?.Request.Headers.UserAgent.ToString();
            return string.IsNullOrEmpty(value) ? null : value.Length > MaxUserAgentLength ? value[..MaxUserAgentLength] : value;
        }
    }

    public string? CorrelationId => Http?.Items.TryGetValue(CorrelationItemKey, out var value) == true ? value as string : null;

    public bool IsInRole(string role) => Http?.User.IsInRole(role) == true;

    /// <summary>Ключ в HttpContext.Items, куда middleware кладёт correlation id.</summary>
    public const string CorrelationItemKey = "CorrelationId";
}
