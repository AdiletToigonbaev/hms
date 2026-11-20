namespace Hms.SharedKernel;

/// <summary>
/// Номер телефона в формате E.164 (<c>+996555123456</c>). Используется для входа, WhatsApp и CRM.
/// Принимает типичные варианты записи в Кыргызстане: <c>0555 12-34-56</c>, <c>996555123456</c>, <c>+996 (555) 123 456</c>.
/// </summary>
public readonly record struct PhoneNumber
{
    private const string KyrgyzstanCode = "996";

    private PhoneNumber(string value)
    {
        Value = value;
    }

    /// <summary>Номер в формате E.164, начинается с «+».</summary>
    public string Value { get; }

    public static Result<PhoneNumber> Parse(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return Invalid();
        }

        var trimmed = input.Trim();
        var hasPlus = trimmed.StartsWith('+');
        Span<char> digits = stackalloc char[trimmed.Length];
        var count = 0;
        foreach (var ch in trimmed.AsSpan(hasPlus ? 1 : 0))
        {
            if (char.IsAsciiDigit(ch))
            {
                digits[count++] = ch;
            }
            else if (ch is not (' ' or '-' or '(' or ')' or '.'))
            {
                return Invalid();
            }
        }

        var number = new string(digits[..count]);

        if (!hasPlus)
        {
            if (number.Length == 10 && number[0] == '0')
            {
                // Национальный формат КР: 0XXX XXXXXX
                number = KyrgyzstanCode + number[1..];
            }
            else if (number.Length == 9)
            {
                // Без ведущего нуля: XXX XXXXXX
                number = KyrgyzstanCode + number;
            }
        }

        // E.164: до 15 цифр, код страны не начинается с 0
        if (number.Length is < 10 or > 15 || number[0] == '0')
        {
            return Invalid();
        }

        if (number.StartsWith(KyrgyzstanCode, StringComparison.Ordinal) && number.Length != 12)
        {
            return Invalid();
        }

        return Result.Success(new PhoneNumber("+" + number));
    }

    public override string ToString() => Value;

    private static Error Invalid() =>
        Error.Validation("phone.invalid", "Некорректный номер телефона. Пример: +996 555 123 456.");
}
