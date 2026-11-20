using System.Globalization;

namespace Hms.SharedKernel;

/// <summary>
/// Денежная сумма. Хранится как <see cref="decimal"/> с точностью 2 знака (в БД — numeric(14,2)).
/// Операции допускаются только между суммами одной валюты. Округление — к ближайшему, половина от нуля.
/// </summary>
public readonly record struct Money : IComparable<Money>
{
    public const string DefaultCurrency = "KGS";

    /// <summary>Максимальное значение, помещающееся в numeric(14,2).</summary>
    public const decimal MaxAbsoluteAmount = 999_999_999_999.99m;

    public Money(decimal amount, string currency = DefaultCurrency)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(currency);
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException("Код валюты — три заглавные латинские буквы (ISO 4217).", nameof(currency));
        }

        var rounded = decimal.Round(amount, 2, MidpointRounding.AwayFromZero);
        if (Math.Abs(rounded) > MaxAbsoluteAmount)
        {
            throw new ArgumentOutOfRangeException(nameof(amount), "Сумма превышает допустимый диапазон numeric(14,2).");
        }

        Amount = rounded;
        Currency = currency;
    }

    public decimal Amount { get; }

    public string Currency { get; }

    public bool IsZero => Amount == 0m;

    public bool IsNegative => Amount < 0m;

    public bool IsPositive => Amount > 0m;

    public static Money Zero(string currency = DefaultCurrency) => new(0m, currency);

    public static Money Kgs(decimal amount) => new(amount, DefaultCurrency);

    public static Money operator +(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount + right.Amount, left.Currency);
    }

    public static Money operator -(Money left, Money right)
    {
        EnsureSameCurrency(left, right);
        return new Money(left.Amount - right.Amount, left.Currency);
    }

    public static Money operator -(Money value) => new(-value.Amount, value.Currency);

    public static Money operator *(Money left, decimal multiplier) => new(left.Amount * multiplier, left.Currency);

    public static Money operator *(decimal multiplier, Money right) => right * multiplier;

    public static bool operator <(Money left, Money right) => left.CompareTo(right) < 0;

    public static bool operator >(Money left, Money right) => left.CompareTo(right) > 0;

    public static bool operator <=(Money left, Money right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Money left, Money right) => left.CompareTo(right) >= 0;

    public Money Add(Money other) => this + other;

    public Money Subtract(Money other) => this - other;

    public Money Multiply(decimal multiplier) => this * multiplier;

    public Money Negate() => -this;

    public int CompareTo(Money other)
    {
        EnsureSameCurrency(this, other);
        return Amount.CompareTo(other.Amount);
    }

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Amount:0.00} {Currency}");

    private static void EnsureSameCurrency(Money left, Money right)
    {
        if (!string.Equals(left.Currency, right.Currency, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Операция над суммами в разных валютах: {left.Currency} и {right.Currency}.");
        }
    }
}
