namespace Hms.Platform.Time;

/// <inheritdoc />
public sealed class HotelClock : IHotelClock
{
    /// <summary>Часовой пояс по умолчанию (ТЗ §2).</summary>
    public const string DefaultTimeZoneId = "Asia/Bishkek";

    private readonly TimeProvider _timeProvider;

    public HotelClock(TimeProvider timeProvider, string timeZoneId = DefaultTimeZoneId)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentException.ThrowIfNullOrWhiteSpace(timeZoneId);
        _timeProvider = timeProvider;
        TimeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }

    public TimeZoneInfo TimeZone { get; }

    public DateTimeOffset UtcNow => _timeProvider.GetUtcNow();

    public DateTimeOffset LocalNow => ToHotelTime(UtcNow);

    public DateOnly Today => DateOnly.FromDateTime(LocalNow.DateTime);

    public DateTimeOffset ToHotelTime(DateTimeOffset instant) => TimeZoneInfo.ConvertTime(instant, TimeZone);

    public DateTimeOffset ToUtc(DateOnly date, TimeOnly time)
    {
        var local = date.ToDateTime(time, DateTimeKind.Unspecified);
        var offset = TimeZone.GetUtcOffset(local);
        return new DateTimeOffset(local, offset).ToUniversalTime();
    }
}
