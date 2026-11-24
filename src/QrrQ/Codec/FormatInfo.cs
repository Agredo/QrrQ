using System.Numerics;

namespace QrrQ.Codec;

/// <summary>Ein gültiges Formatwort mit seinem Abstand zu den gelesenen Bits.</summary>
internal readonly record struct FormatMatch(int Bits, EccLevel Ecc, int Mask, int Distance, bool Unique);

/// <summary>
/// Formatinformation (BCH(15,5), 2 Kopien) und Versionsinformation (BCH(18,6)).
/// </summary>
/// <remarks>
/// Kernbeobachtung für QrrQ: Transponiert man den Code, werden beide Formatkopien bitweise umgekehrt
/// gelesen (Kopie 2 tauscht dabei Bit 7 mit dem Dark Module). Keines der 32 gültigen Formatwörter ist
/// nach dieser Umkehrung wieder gültig (Hamming-Abstand ≥ 3) – die Belegung beider Kopien plant daher
/// <see cref="FormatPlanner"/>.
/// </remarks>
internal static class FormatInfo
{
    private static readonly FormatMatch[] All = BuildAll();

    public static int Encode(EccLevel ecc, int mask)
    {
        var data = ecc.FormatBits << 3 | mask;
        var remainder = data;
        for (var i = 0; i < 10; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 9) * 0x537);
        }

        return (data << 10 | remainder) ^ 0x5412;
    }

    public static (int Row, int Column) Copy1Cell(int bit) => bit switch
    {
        <= 5 => (bit, 8),
        6 => (7, 8),
        7 => (8, 8),
        8 => (8, 7),
        _ => (8, 14 - bit),
    };

    public static (int Row, int Column) Copy2Cell(int size, int bit) =>
        bit <= 7 ? (8, size - 1 - bit) : (size - 15 + bit, 8);

    public static int Read(ReadOnlySpan<bool> modules, int size, int copy, bool transposed = false)
    {
        var bits = 0;
        for (var bit = 0; bit < 15; bit++)
        {
            var (row, column) = copy == 1 ? Copy1Cell(bit) : Copy2Cell(size, bit);
            if (transposed)
            {
                (row, column) = (column, row);
            }

            if (modules[row * size + column])
            {
                bits |= 1 << bit;
            }
        }

        return bits;
    }

    public static void Draw(Span<bool> modules, int size, int bits, int copy, bool transposed)
    {
        for (var bit = 0; bit < 15; bit++)
        {
            var (row, column) = copy == 1 ? Copy1Cell(bit) : Copy2Cell(size, bit);
            if (transposed)
            {
                (row, column) = (column, row);
            }

            modules[row * size + column] = (bits >> bit & 1) != 0;
        }
    }

    /// <summary>Nächstes gültiges Formatwort zu beliebigen 15 Bits.</summary>
    public static FormatMatch Nearest(int bits)
    {
        var best = All[0] with { Distance = int.MaxValue };
        var unique = true;
        foreach (var candidate in All)
        {
            var distance = BitOperations.PopCount((uint)(candidate.Bits ^ bits));
            if (distance < best.Distance)
            {
                best = candidate with { Distance = distance };
                unique = true;
            }
            else if (distance == best.Distance)
            {
                unique = false;
            }
        }

        return best with { Unique = unique };
    }

    /// <summary>Dekodiert wie ein Scanner: exakter Treffer in einer Kopie gewinnt, sonst kleinster Abstand ≤ 3.</summary>
    public static FormatMatch? Decode(int copy1, int copy2)
    {
        foreach (var candidate in All)
        {
            if (candidate.Bits == copy1 || candidate.Bits == copy2)
            {
                return candidate;
            }
        }

        var first = Nearest(copy1);
        var second = Nearest(copy2);
        var best = first.Distance <= second.Distance ? first : second;
        return best.Distance <= 3 ? best : null;
    }

    public static int VersionBits(int version)
    {
        var remainder = version;
        for (var i = 0; i < 12; i++)
        {
            remainder = (remainder << 1) ^ ((remainder >> 11) * 0x1F25);
        }

        return version << 12 | remainder;
    }

    private static FormatMatch[] BuildAll()
    {
        var result = new FormatMatch[32];
        var index = 0;
        foreach (var ecc in Enum.GetValues<EccLevel>())
        {
            for (var mask = 0; mask < 8; mask++)
            {
                result[index++] = new FormatMatch(Encode(ecc, mask), ecc, mask, 0, true);
            }
        }

        return result;
    }
}
