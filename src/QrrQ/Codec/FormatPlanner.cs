using System.Numerics;

namespace QrrQ.Codec;

/// <summary>
/// Content of the two format fields: <see cref="Copy1"/>/<see cref="Copy2"/> are the 15 bits drawn into the
/// cells of copy 1 and copy 2. The mirrored direction sees the same cells bit-reversed.
/// </summary>
/// <param name="Copy1">Bits for the cells of format copy 1.</param>
/// <param name="Copy2">Bits for the cells of format copy 2 (bit 7 is the transposed dark module, so always 1).</param>
/// <param name="DistanceNormal">Distance of the best copy to the format word of the normal direction (0 = exact).</param>
/// <param name="DistanceMirrored">The same for the mirrored direction.</param>
/// <param name="Tolerance">
/// How many additional read errors in the format area each copy tolerates before a decoder could correct it into a
/// wrong format word or the correct copy becomes uncorrectable.
/// </param>
internal readonly record struct FormatPlan(int Copy1, int Copy2, int DistanceNormal, int DistanceMirrored, int Tolerance)
{
    /// <summary>
    /// Smaller is better: first closeness to the format word (zxing-cpp additionally checks unmasked and mirrored
    /// readings - a distance of 2 can already be undercut there), then robustness against sampling errors.
    /// </summary>
    public int Cost => (DistanceNormal + DistanceMirrored) * 16 + (3 - Tolerance);
}

/// <summary>
/// Looks for a format assignment per mask pair that the common decoder strategies read correctly
/// in both directions.
/// </summary>
/// <remarks>
/// No valid format word is valid again when bit-reversed. Each direction therefore carries its format word in one
/// copy, and the "foreign" copy must be placed so that it does not mislead any decoder.
/// The rules were measured against real decoders:
/// <list type="bullet">
/// <item>OpenCV fails as soon as any copy is correctable into a wrong format word.</item>
/// <item>ZBar combines the halves of both copies by majority vote; wrong copies flip the result.</item>
/// <item>zxing-cpp additionally rates the mirrored reading and takes the smallest distance - both directions
///       therefore need the same distance so that the normal reading wins the tie.</item>
/// </list>
/// Foreign copies are thus made "uncorrectable" (>= 4 bits away from every format word). To achieve that,
/// individual bits are shifted; usually each direction then reads its format word with 1 bit of deviation -
/// which every decoder corrects, because the format information tolerates up to 3 bit errors.
/// </remarks>
internal static class FormatPlanner
{
    private const int Words = 1 << 15;
    private static readonly byte[] NearestCode = new byte[Words];
    private static readonly byte[] NearestDistance = new byte[Words];
    private static readonly int[] Codes = new int[32];
    private static readonly FormatPlan?[]?[] Cache = new FormatPlan?[16][];

    static FormatPlanner()
    {
        var index = 0;
        foreach (var ecc in Enum.GetValues<EccLevel>())
        {
            for (var mask = 0; mask < 8; mask++)
            {
                Codes[index++] = FormatInfo.Encode(ecc, mask);
            }
        }

        for (var word = 0; word < Words; word++)
        {
            var best = 0;
            var bestDistance = int.MaxValue;
            for (var c = 0; c < Codes.Length; c++)
            {
                var distance = BitOperations.PopCount((uint)(Codes[c] ^ word));
                if (distance < bestDistance)
                {
                    (best, bestDistance) = (c, distance);
                }
            }

            NearestCode[word] = (byte)best;
            NearestDistance[word] = (byte)bestDistance;
        }
    }

    public static FormatPlan? Get(EccLevel normal, EccLevel mirrored, int maskNormal, int maskMirrored)
    {
        var plans = Cache[(int)normal * 4 + (int)mirrored] ??= Build(normal, mirrored);
        return plans[maskNormal * 8 + maskMirrored];
    }

    public static int Reverse(int bits)
    {
        var result = 0;
        for (var i = 0; i < 15; i++)
        {
            result |= (bits >> i & 1) << (14 - i);
        }

        return result;
    }

    private static FormatPlan?[] Build(EccLevel normal, EccLevel mirrored)
    {
        var plans = new FormatPlan?[64];
        for (var maskA = 0; maskA < 8; maskA++)
        {
            for (var maskB = 0; maskB < 8; maskB++)
            {
                plans[maskA * 8 + maskB] = Plan(FormatInfo.Encode(normal, maskA), FormatInfo.Encode(mirrored, maskB));
            }
        }

        return plans;
    }

    private static FormatPlan? Plan(int formatA, int formatB)
    {
        // Candidates: everything within 2 bits of the own or the (reversed) foreign format word. Each copy must be
        // either correctable into the right format word in BOTH readings or be >= 4 bits away from all of them.
        int[] copies1 =
        [
            .. Ball(formatA, 2).Concat(Ball(Reverse(formatB), 2)).Distinct()
                .Where(c => Acceptable(c, formatA) && Acceptable(Reverse(c), formatB)),
        ];
        int[] copies2 = [.. copies1.Where(c => (c >> 7 & 1) != 0)]; // bit 7 = dark module of the mirrored reading

        FormatPlan? best = null;
        foreach (var copy1 in copies1)
        {
            foreach (var copy2 in copies2)
            {
                var distanceA = BestDistance(copy1, copy2, formatA);
                var distanceB = BestDistance(Reverse(copy1), Reverse(copy2), formatB);

                // Same distance in both directions: decoders that also rate the mirrored reading
                // (zxing-cpp) must never prefer the "wrong" direction.
                if (distanceA > 3 || distanceA != distanceB ||
                    !WinsZbarVote(copy1, copy2, formatA) ||
                    !WinsZbarVote(Reverse(copy1), Reverse(copy2), formatB))
                {
                    continue;
                }

                var margin = Math.Min(
                    Math.Min(WrongDistance(copy1, formatA), WrongDistance(copy2, formatA)),
                    Math.Min(WrongDistance(Reverse(copy1), formatB), WrongDistance(Reverse(copy2), formatB))) - 4;
                var plan = new FormatPlan(copy1, copy2, distanceA, distanceB, Math.Min(margin, 3 - distanceA));
                if (best is null || plan.Cost < best.Value.Cost)
                {
                    best = plan;
                }
            }
        }

        return best;
    }

    /// <summary>
    /// A copy is acceptable if it is correctable into the format word <paramref name="target"/> (&lt;= 3 bits) or
    /// is at least 4 bits away from every format word ("uncorrectable", gets discarded). A copy that could be
    /// corrected into a <i>wrong</i> format word is off limits - OpenCV and ZBar provably fail on that even when
    /// the other copy is exact.
    /// </summary>
    private static bool Acceptable(int copy, int target) =>
        NearestDistance[copy] > 3 || Codes[NearestCode[copy]] == target;

    private static int BestDistance(int copy1, int copy2, int target)
    {
        var best = int.MaxValue;
        foreach (var copy in (ReadOnlySpan<int>)[copy1, copy2])
        {
            if (NearestDistance[copy] <= 3 && Codes[NearestCode[copy]] == target)
            {
                best = Math.Min(best, NearestDistance[copy]);
            }
        }

        return best;
    }

    /// <summary>Distance to the nearest format word that is <i>not</i> the target.</summary>
    private static int WrongDistance(int copy, int target)
    {
        var best = int.MaxValue;
        foreach (var code in Codes)
        {
            if (code != target)
            {
                best = Math.Min(best, BitOperations.PopCount((uint)(code ^ copy)));
            }
        }

        return best;
    }

    /// <summary>
    /// Replica of ZBar's <c>qr_finder_fmt_info_decode</c>: bits 0-7 and 8-14 are read from both
    /// copies separately and combined (up to four words). Every word votes - uncorrectable ones too, then with
    /// "4 errors" for their raw data bits. More votes beat fewer errors.
    /// </summary>
    private static bool WinsZbarVote(int copy1, int copy2, int target)
    {
        const int low = 0x00FF, high = 0x7F00, mask = 0x5412;
        Span<int> lo = [copy1 & low, copy2 & low];
        Span<int> hi = [copy1 & high, copy2 & high];
        var limit = 2 << (hi[0] != hi[1] ? 1 : 0);
        var step = lo[0] == lo[1] ? 2 : 1;

        Span<int> values = stackalloc int[4];
        Span<int> votes = stackalloc int[4];
        Span<int> errors = stackalloc int[4];
        var distinct = 0;
        for (var i = 0; i < limit; i += step)
        {
            var word = lo[i & 1] | hi[i >> 1];
            var correctable = NearestDistance[word] <= 3;
            var value = ((correctable ? Codes[NearestCode[word]] : word) ^ mask) >> 10;
            var wordErrors = correctable ? NearestDistance[word] : 4;

            var slot = values[..distinct].IndexOf(value);
            if (slot < 0)
            {
                slot = distinct++;
                values[slot] = value;
                errors[slot] = wordErrors;
            }
            else
            {
                errors[slot] = Math.Min(errors[slot], wordErrors);
            }

            votes[slot]++;
        }

        var best = 0;
        for (var k = 1; k < distinct; k++)
        {
            if ((errors[best] > 3 && errors[k] <= 3) || votes[k] > votes[best] ||
                (votes[k] == votes[best] && errors[k] < errors[best]))
            {
                best = k;
            }
        }

        return errors[best] < 4 && values[best] == (target ^ mask) >> 10;
    }

    private static IEnumerable<int> Ball(int center, int radius)
    {
        yield return center;
        if (radius >= 1)
        {
            for (var i = 0; i < 15; i++)
            {
                yield return center ^ 1 << i;
            }
        }

        if (radius >= 2)
        {
            for (var i = 0; i < 15; i++)
            {
                for (var j = i + 1; j < 15; j++)
                {
                    yield return center ^ 1 << i ^ 1 << j;
                }
            }
        }
    }
}
