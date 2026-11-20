namespace Hms.Modules.Identity.Contracts;

/// <summary>Четыре роли системы (ТЗ §3).</summary>
public static class Roles
{
    public const string Owner = "Owner";
    public const string Administrator = "Administrator";
    public const string Housekeeper = "Housekeeper";
    public const string Guest = "Guest";

    public static readonly IReadOnlyList<string> All = [Owner, Administrator, Housekeeper, Guest];

    /// <summary>Роли персонала, которых создаёт владелец.</summary>
    public static readonly IReadOnlyList<string> ManageableStaff = [Administrator, Housekeeper];

    public static string DisplayName(string role) => role switch
    {
        Owner => "Владелец",
        Administrator => "Администратор",
        Housekeeper => "Уборщик",
        Guest => "Гость",
        _ => role,
    };
}

public enum UserType
{
    Staff,
    Guest,
}
