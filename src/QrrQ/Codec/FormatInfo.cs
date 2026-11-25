using System.Numerics;

namespace QrrQ.Codec;

/// <summary>A valid format word together with its distance to the read bits.</summary>
internal readonly record struct FormatMatch(int Bits, EccLevel Ecc, int Mask, int Distance, bool Unique);

/// <summary>
/// Format information (BCH(15,5), 2 copies) and version information (BCH(18,6)).
/// </summary>
/// <remarks>
/// Key observation for QrrQ: when the code is transposed, both format copies are read bit-reversed
/// (copy 2 also swaps bit 7 with the dark module). None of the 32 valid format words is valid again
/// after that reversal (Hamming distance >= 3) - so the content of both copies is planned by
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

    /// <summary>Nearest valid format word to arbitrary 15 bits.</summary>
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

    /// <summary>Decodes like a scanner: an exact hit in one copy wins, otherwise the smallest distance &lt;= 3.</summary>
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
