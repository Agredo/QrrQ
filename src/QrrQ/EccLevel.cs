namespace QrrQ;

/// <summary>Error correction level according to ISO/IEC 18004.</summary>
public enum EccLevel
{
    /// <summary>L - about 7% of the codewords can be reconstructed.</summary>
    Low,

    /// <summary>M - about 15% of the codewords can be reconstructed.</summary>
    Medium,

    /// <summary>Q - about 25% of the codewords can be reconstructed.</summary>
    Quartile,

    /// <summary>H - about 30% of the codewords can be reconstructed.</summary>
    High,
}

/// <summary>Extension members (C# 14) for <see cref="EccLevel"/>.</summary>
public static class EccLevelExtensions
{
    extension(EccLevel level)
    {
        /// <summary>Single-letter code of the level: L, M, Q or H.</summary>
        public char Letter => level switch
        {
            EccLevel.Low => 'L',
            EccLevel.Medium => 'M',
            EccLevel.Quartile => 'Q',
            EccLevel.High => 'H',
            _ => throw new ArgumentOutOfRangeException(nameof(level)),
        };

        /// <summary>The 2-bit identifier in the format field (L = 01, M = 00, Q = 11, H = 10).</summary>
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
        /// <summary>Reads a level from its single-letter code (L, M, Q, H).</summary>
        public static EccLevel FromLetter(char letter) => char.ToUpperInvariant(letter) switch
        {
            'L' => EccLevel.Low,
            'M' => EccLevel.Medium,
            'Q' => EccLevel.Quartile,
            'H' => EccLevel.High,
            _ => throw new FormatException($"Unknown error correction level '{letter}'. Allowed: L, M, Q, H."),
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
