namespace Hms.Platform.Time;

/// <summary>
/// Единственный источник текущего времени в приложении.
/// События хранятся в UTC; даты проживания и «сегодня» — в часовом поясе отеля (Asia/Bishkek).
/// </summary>
public interface IHotelClock
{
    /// <summary>Часовой пояс отеля.</summary>
    TimeZoneInfo TimeZone { get; }

    /// <summary>Текущий момент в UTC.</summary>
    DateTimeOffset UtcNow { get; }

    /// <summary>Текущие дата и время в часовом поясе отеля.</summary>
    DateTimeOffset LocalNow { get; }

    /// <summary>Сегодняшняя дата по часовому поясу отеля.</summary>
    DateOnly Today { get; }

    /// <summary>Переводит момент времени в часовой пояс отеля.</summary>
    DateTimeOffset ToHotelTime(DateTimeOffset instant);

    /// <summary>Переводит локальные дату и время отеля (например, «заезд 14:00») в момент UTC.</summary>
    DateTimeOffset ToUtc(DateOnly date, TimeOnly time);
}
