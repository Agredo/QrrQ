namespace QrrQ;

/// <summary>Fehlerkorrekturstufe nach ISO/IEC 18004.</summary>
public enum EccLevel
{
    /// <summary>L – ca. 7 % der Codewörter rekonstruierbar.</summary>
    Low,

    /// <summary>M – ca. 15 % der Codewörter rekonstruierbar.</summary>
    Medium,

    /// <summary>Q – ca. 25 % der Codewörter rekonstruierbar.</summary>
    Quartile,

    /// <summary>H – ca. 30 % der Codewörter rekonstruierbar.</summary>
    High,
}

/// <summary>Erweiterungsmember (C# 14) für <see cref="EccLevel"/>.</summary>
public static class EccLevelExtensions
{
    extension(EccLevel level)
    {
        /// <summary>Kennbuchstabe der Stufe: L, M, Q oder H.</summary>
        public char Letter => level switch
        {
            EccLevel.Low => 'L',
            EccLevel.Medium => 'M',
            EccLevel.Quartile => 'Q',
            EccLevel.High => 'H',
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

        /// <summary>Die 2-Bit-Kennung im Formatfeld (L = 01, M = 00, Q = 11, H = 10).</summary>
        internal int FormatBits => level switch
        {
            EccLevel.Low => 0b01,
            EccLevel.Medium => 0b00,
            EccLevel.Quartile => 0b11,
            EccLevel.High => 0b10,
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };
    }

    extension(EccLevel)
    {
        /// <summary>Liest eine Stufe aus ihrem Kennbuchstaben (L, M, Q, H).</summary>
        public static EccLevel FromLetter(char letter) => char.ToUpperInvariant(letter) switch
        {
            'L' => EccLevel.Low,
            'M' => EccLevel.Medium,
            'Q' => EccLevel.Quartile,
            'H' => EccLevel.High,
            _ => throw new FormatException($"Unbekannte Fehlerkorrekturstufe '{letter}'. Erlaubt: L, M, Q, H."),
        };

        internal static EccLevel FromFormatBits(int bits) => bits switch
        {
            0b01 => EccLevel.Low,
            0b00 => EccLevel.Medium,
            0b11 => EccLevel.Quartile,
            _ => EccLevel.High,
        };
    }
}
