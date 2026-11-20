namespace Hms.Platform.Security;

/// <summary>Типы claims в access-токене (короткие имена JWT, без маппинга в длинные URI .NET).</summary>
public static class HmsClaimTypes
{
    public const string Subject = "sub";
    public const string Name = "name";
    public const string Email = "email";
    public const string Role = "role";
    public const string UserType = "utype";
    public const string SecurityStamp = "sst";
}
