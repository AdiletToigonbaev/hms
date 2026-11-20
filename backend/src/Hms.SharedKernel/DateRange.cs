namespace Hms.SharedKernel;

/// <summary>
/// Полуоткрытый интервал дат [Start, End): дата заезда входит, дата выезда — нет.
/// Соответствует типу PostgreSQL <c>daterange</c> с границами <c>[)</c>, на котором построено
/// ограничение против пересекающихся броней.
/// </summary>
public readonly record struct DateRange
{
    public DateRange(DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            throw new ArgumentException("Дата окончания должна быть позже даты начала (минимум одна ночь).", nameof(end));
        }

        Start = start;
        End = end;
    }

    /// <summary>Первая ночь (дата заезда).</summary>
    public DateOnly Start { get; }

    /// <summary>Дата выезда — в интервал не входит.</summary>
    public DateOnly End { get; }

    /// <summary>Количество ночей.</summary>
    public int Nights => End.DayNumber - Start.DayNumber;

    /// <summary>Пытается создать интервал; возвращает ошибку валидации вместо исключения.</summary>
    public static Result<DateRange> Create(DateOnly start, DateOnly end) =>
        end > start
            ? Result.Success(new DateRange(start, end))
            : Error.Validation("date_range.invalid", "Дата выезда должна быть позже даты заезда.");

    /// <summary>Пересекаются ли интервалы. Выезд в день заезда другой брони пересечением не считается.</summary>
    public bool Overlaps(DateRange other) => Start < other.End && other.Start < End;

    /// <summary>Входит ли ночь с указанной даты в интервал.</summary>
    public bool Contains(DateOnly night) => night >= Start && night < End;

    /// <summary>Перечисляет даты всех ночей интервала.</summary>
    public IEnumerable<DateOnly> EachNight()
    {
        for (var d = Start; d < End; d = d.AddDays(1))
        {
            yield return d;
        }
    }

    public override string ToString() => $"[{Start:yyyy-MM-dd}, {End:yyyy-MM-dd})";
}
