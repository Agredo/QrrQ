using System.Numerics;

namespace QrrQ.Codec;

/// <summary>
/// Belegung der beiden Formatfelder: <see cref="Copy1"/>/<see cref="Copy2"/> sind die 15 Bits, die in die
/// Zellen von Kopie 1 bzw. Kopie 2 gezeichnet werden. Die gespiegelte Richtung sieht dieselben Zellen
/// bitweise umgekehrt.
/// </summary>
/// <param name="Copy1">Bits für die Zellen der Formatkopie 1.</param>
/// <param name="Copy2">Bits für die Zellen der Formatkopie 2 (Bit 7 ist das transponierte Dark Module, also immer 1).</param>
/// <param name="DistanceNormal">Abstand der besten Kopie zum Formatwort der normalen Richtung (0 = exakt).</param>
/// <param name="DistanceMirrored">Dasselbe für die gespiegelte Richtung.</param>
/// <param name="Tolerance">
/// Wie viele zusätzliche Lesefehler im Formatbereich jede Kopie verträgt, bevor ein Decoder sie zu einem
/// falschen Formatwort korrigieren könnte oder die richtige Kopie unkorrigierbar wird.
/// </param>
internal readonly record struct FormatPlan(int Copy1, int Copy2, int DistanceNormal, int DistanceMirrored, int Tolerance)
{
    /// <summary>
    /// Kleiner ist besser: zuerst Nähe zum Formatwort (zxing-cpp prüft zusätzlich ungemaskierte und gespiegelte
    /// Lesarten – ein Abstand von 2 kann dort schon unterboten werden), dann Robustheit gegen Abtastfehler.
    /// </summary>
    public int Cost => (DistanceNormal + DistanceMirrored) * 16 + (3 - Tolerance);
}

/// <summary>
/// Sucht für jedes Maskenpaar eine Formatbelegung, die von den verbreiteten Decoder-Strategien
/// in beiden Richtungen richtig gelesen wird.
/// </summary>
/// <remarks>
/// Kein gültiges Formatwort ist bitweise umgekehrt wieder gültig. Deshalb trägt jede Richtung ihr Formatwort
/// in einer Kopie, und die jeweils „fremde“ Kopie muss so liegen, dass sie keinen Decoder in die Irre führt.
/// Die Regeln wurden an echten Decodern gemessen:
/// <list type="bullet">
/// <item>OpenCV scheitert, sobald irgendeine Kopie zu einem falschen Formatwort korrigierbar ist.</item>
/// <item>ZBar kombiniert die Hälften beider Kopien per Mehrheitsentscheid; falsche Kopien kippen das Ergebnis.</item>
/// <item>zxing-cpp bewertet zusätzlich die gespiegelte Lesart und nimmt den kleinsten Abstand – beide
///       Richtungen brauchen daher denselben Abstand, damit die normale Lesart den Gleichstand gewinnt.</item>
/// </list>
/// Fremde Kopien werden darum „unkorrigierbar“ gemacht (≥ 4 Bit von jedem Formatwort). Dafür werden einzelne
/// Bits verschoben; meist liest dann jede Richtung ihr Formatwort mit 1 Bit Abweichung – das korrigiert jeder
/// Decoder, denn die Formatinformation verträgt bis zu 3 Bitfehler.
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
        // Kandidaten: alles bis 2 Bit um das eigene bzw. das (umgekehrte) fremde Formatwort. Jede Kopie muss in
        // BEIDEN Lesarten entweder zum richtigen Formatwort korrigierbar oder von allen ≥ 4 Bit entfernt sein.
        int[] copies1 =
        [
            .. Ball(formatA, 2).Concat(Ball(Reverse(formatB), 2)).Distinct()
                .Where(c => Acceptable(c, formatA) && Acceptable(Reverse(c), formatB)),
        ];
        int[] copies2 = [.. copies1.Where(c => (c >> 7 & 1) != 0)]; // Bit 7 = Dark Module der gespiegelten Lesart

        FormatPlan? best = null;
        foreach (var copy1 in copies1)
        {
            foreach (var copy2 in copies2)
            {
                var distanceA = BestDistance(copy1, copy2, formatA);
                var distanceB = BestDistance(Reverse(copy1), Reverse(copy2), formatB);

                // Gleicher Abstand in beiden Richtungen: Decoder, die auch die gespiegelte Lesart
                // bewerten (zxing-cpp), sollen nie die „falsche“ Richtung bevorzugen.
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
    /// Eine Kopie ist zulässig, wenn sie zum Formatwort <paramref name="target"/> korrigierbar ist (≤ 3 Bit) oder
    /// von allen Formatwörtern ≥ 4 Bit entfernt („unkorrigierbar“, wird verworfen). Eine Kopie, die sich zu einem
    /// <i>falschen</i> Formatwort korrigieren ließe, ist tabu – OpenCV und ZBar scheitern daran nachweislich,
    /// selbst wenn die andere Kopie exakt ist.
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

    /// <summary>Abstand zum nächsten Formatwort, das <i>nicht</i> das Ziel ist.</summary>
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
    /// Nachbildung von ZBars <c>qr_finder_fmt_info_decode</c>: Bits 0–7 und 8–14 werden getrennt aus beiden
    /// Kopien gelesen und kombiniert (bis zu vier Wörter). Jedes Wort stimmt ab – auch unkorrigierbare, dann mit
    /// „4 Fehlern“ für seine rohen Datenbits. Mehr Stimmen schlagen weniger Fehler.
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
