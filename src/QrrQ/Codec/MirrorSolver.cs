using System.Numerics;
using System.Runtime.InteropServices;
using QrrQ.Algebra;
using QrrQ.Reading;

namespace QrrQ.Codec;

internal sealed record SolverSettings(int MaxCorrectedCodewords, int Attempts, Random Random);

internal sealed record MirrorSolution(
    QrMatrix Matrix,
    QrReadResult Normal,
    QrReadResult Mirrored,
    FormatPlan Format,
    int Penalty)
{
    public int CorrectedTotal => Normal.CorrectedCodewords + Mirrored.CorrectedCodewords;

    /// <summary>Kleiner ist besser: erst Fehlerfreiheit der Daten, dann exakte Formatinfo, dann Optik.</summary>
    public long Score => CorrectedTotal * 1_000_000L + Format.Cost * 10_000L + Penalty;
}

/// <summary>
/// Findet ein Modulraster M, das normal gelesen Text A und transponiert (= gespiegelt und gedreht)
/// gelesen Text B ergibt.
/// </summary>
/// <remarks>
/// <para>
/// Unbekannte sind die Datenmodule x₀ … x_{D-1} von M (Zickzack-Reihenfolge). Für Richtung A ist
/// Strombit k = x_k ⊕ maskA(k); für Richtung B ist Strombit k = x_{σ(k)} ⊕ maskB(k), wobei σ die
/// Transposition auf den Datenmodulen ist. Beides ist affin in x.
/// </para>
/// <para>
/// Bedingungen: (1) die Nachrichtenbits beider Texte (fixieren einzelne x), (2) alle Reed-Solomon-Syndrome
/// beider Richtungen sind 0. Weil GF(256)-Multiplikation mit einer Konstanten GF(2)-linear ist, zerfällt jedes
/// Syndrom in 8 Bitgleichungen. Freiheitsgrade liefern die Füllbytes nach dem Terminator, die Scanner ignorieren.
/// </para>
/// <para>
/// Die rechte Seite hängt nur über Konstanten von den Masken ab. Alle 8 × 8 Masken-Kombinationen werden daher als
/// 64 Spuren eines <see cref="ulong"/> in <b>einer</b> Elimination gelöst.
/// </para>
/// <para>
/// Unvermeidbar ist eine kleine Überlappung: Beide Nachrichten beginnen unten rechts, und diese Ecke bildet die
/// Transposition auf sich selbst ab. Dort müssen die Bits beider Texte übereinstimmen – das regelt die Maskenwahl.
/// Gelingt das nicht, darf (optional) ein Codewort pro Konflikt einer Seite „geopfert“ werden; der Scanner
/// repariert es dann per Fehlerkorrektur.
/// </para>
/// </remarks>
internal sealed class MirrorSolver
{
    private const ulong AllLanes = ulong.MaxValue;
    private const int MaxLanesWithCorrections = 8;
    private const int MaxEncodingVariants = 32;
    private const int MaxEliminations = 6;
    private static readonly HashSet<int> NoBytes = [];

    private readonly SymbolLayout layout;
    private readonly BlockLayout blocksA;
    private readonly BlockLayout blocksB;
    private readonly Payload payloadA;
    private readonly Payload payloadB;
    private readonly SolverSettings settings;
    private readonly int variables;
    private readonly ulong[] maskLanesA;
    private readonly ulong[] maskLanesB;
    private sbyte[] messageA = [];
    private sbyte[] messageB = [];
    private readonly int[] streamBitB;
    private readonly ulong validLanes;

    private MirrorSolver(int version, EccLevel eccA, EccLevel eccB, Payload a, Payload b, SolverSettings settings)
    {
        layout = SymbolLayout.Get(version);
        blocksA = BlockLayout.Get(version, eccA);
        blocksB = BlockLayout.Get(version, eccB);
        payloadA = a;
        payloadB = b;
        this.settings = settings;
        variables = layout.DataPositions.Length;

        // Spur = 8 · MaskeA + MaskeB. Bit „Spur“ gesetzt ⇔ die Maske ist an diesem Modul dunkel.
        maskLanesA = new ulong[variables];
        maskLanesB = new ulong[variables];
        for (var k = 0; k < variables; k++)
        {
            var cell = layout.DataPositions[k];
            int row = cell / layout.Size, column = cell % layout.Size;
            for (var mask = 0; mask < 8; mask++)
            {
                if (Masks.IsDark(mask, row, column))
                {
                    maskLanesA[k] |= 0xFFUL << (8 * mask);
                    maskLanesB[k] |= 0x0101010101010101UL << mask;
                }
            }
        }

        streamBitB = new int[variables];
        for (var s = 0; s < variables; s++)
        {
            streamBitB[layout.TransposedVariable[s]] = s;
        }

        // Nur Maskenpaare, für die sich eine scannerfeste Formatbelegung findet.
        for (var maskA = 0; maskA < 8; maskA++)
        {
            for (var maskB = 0; maskB < 8; maskB++)
            {
                if (FormatPlanner.Get(eccA, eccB, maskA, maskB) is not null)
                {
                    validLanes |= 1UL << (8 * maskA + maskB);
                }
            }
        }
    }

    /// <summary>Grobe Vorprüfung: Passen beide Texte, und bleiben genug Freiheitsgrade?</summary>
    public static bool Fits(int version, EccLevel eccA, EccLevel eccB, Payload a, Payload b)
    {
        var blocksA = BlockLayout.Get(version, eccA);
        var blocksB = BlockLayout.Get(version, eccB);
        var bitsA = a.BitLength(version);
        var bitsB = b.BitLength(version);
        if (bitsA < 0 || bitsB < 0 || bitsA > blocksA.DataCodewords * 8 || bitsB > blocksB.DataCodewords * 8)
        {
            return false;
        }

        var fixedBits = Math.Min(bitsA + 11, blocksA.DataCodewords * 8) + Math.Min(bitsB + 11, blocksB.DataCodewords * 8);
        var equations = 8 * (blocksA.TotalCodewords - blocksA.DataCodewords) + 8 * (blocksB.TotalCodewords - blocksB.DataCodewords);
        return QrTables.RawDataModules(version) - fixedBits >= equations;
    }

    /// <summary>Gibt es in dieser Version überhaupt Platz für zwei Inhalte (unabhängig von den Texten)?</summary>
    public static bool HasHeadroom(int version, EccLevel eccA, EccLevel eccB)
    {
        var total = QrTables.TotalCodewords(version);
        var data = BlockLayout.Get(version, eccA).DataCodewords + BlockLayout.Get(version, eccB).DataCodewords;
        return data - total >= 3;
    }

    public static MirrorSolution? Solve(int version, EccLevel eccA, EccLevel eccB, Payload a, Payload b, SolverSettings settings, bool withCorrections)
    {
        var solver = new MirrorSolver(version, eccA, eccB, a, b, settings);
        return withCorrections ? solver.SolveWithCorrections() : solver.SolveErrorFree();
    }

    private static sbyte[] MessageBits(BitBuffer bits, BlockLayout blocks)
    {
        var result = new sbyte[QrTables.RawDataModules(blocks.Version)];
        Array.Fill(result, (sbyte)-1);
        for (var m = 0; m < bits.Count; m++)
        {
            var streamBit = 8 * blocks.DataStreamIndex[m >> 3] + (m & 7);
            result[streamBit] = bits[m] ? (sbyte)1 : (sbyte)0;
        }

        return result;
    }

    private void UseEncodings(BitBuffer bitsA, BitBuffer bitsB)
    {
        messageA = MessageBits(bitsA, blocksA);
        messageB = MessageBits(bitsB, blocksB);
    }

    /// <summary>Spuren, in denen ein von beiden Nachrichten belegtes Modul zwei verschiedene Werte bräuchte.</summary>
    private ulong ConflictLanes()
    {
        var conflicts = 0UL;
        for (var k = 0; k < variables; k++)
        {
            if (FixedByA(k) && FixedByB(k))
            {
                conflicts |= ValueA(k) ^ ValueB(k);
            }
        }

        return conflicts;
    }

    private bool FixedByA(int k) => messageA[k] >= 0;

    private bool FixedByB(int k) => messageB[streamBitB[k]] >= 0;

    /// <summary>Modulwert x_k (pro Spur), mit dem Richtung A ihr Nachrichtenbit sieht.</summary>
    private ulong ValueA(int k) => (messageA[k] == 1 ? AllLanes : 0) ^ maskLanesA[k];

    /// <summary>Modulwert x_k (pro Spur), mit dem Richtung B ihr Nachrichtenbit sieht.</summary>
    private ulong ValueB(int k)
    {
        var s = streamBitB[k];
        return (messageB[s] == 1 ? AllLanes : 0) ^ maskLanesB[s];
    }

    private MirrorSolution? SolveErrorFree()
    {
        var capacityA = blocksA.DataCodewords * 8;
        var capacityB = blocksB.DataCodewords * 8;
        var variantsA = payloadA.EncodeVariants(layout.Version, capacityA, MaxEncodingVariants).ToArray();
        var variantsB = payloadB.EncodeVariants(layout.Version, capacityB, MaxEncodingVariants).ToArray();

        // Kurze Kodierungen zuerst; die Ein-Segment-Kodierung beider Texte ist immer das erste Paar.
        var pairs =
            from a in variantsA.Index()
            from b in variantsB.Index()
            orderby a.Item.Count + b.Item.Count, a.Index + b.Index
            select (A: a.Item, B: b.Item);

        var eliminations = 0;
        foreach (var (bitsA, bitsB) in pairs)
        {
            UseEncodings(bitsA, bitsB);
            var lanes = validLanes & ~ConflictLanes();
            if (lanes == 0)
            {
                continue;
            }

            var system = Build(NoBytes, NoBytes, out var plan);
            var reduced = system.Reduce();
            lanes &= ~reduced.InconsistentLanes;
            if (lanes != 0 && Pick(reduced, plan, lanes) is { } solution)
            {
                return solution;
            }

            if (++eliminations >= MaxEliminations)
            {
                break;
            }
        }

        return null;
    }

    private MirrorSolution? SolveWithCorrections()
    {
        UseEncodings(
            payloadA.Encode(layout.Version, blocksA.DataCodewords * 8),
            payloadB.Encode(layout.Version, blocksB.DataCodewords * 8));

        List<int> overlap = [];
        for (var k = 0; k < variables; k++)
        {
            if (FixedByA(k) && FixedByB(k))
            {
                overlap.Add(k);
            }
        }

        List<(int Lane, HashSet<int> A, HashSet<int> B)> candidates = [];
        for (var lanes = validLanes; lanes != 0; lanes &= lanes - 1)
        {
            var lane = BitOperations.TrailingZeroCount(lanes);
            List<(int A, int B)> conflicts = [];
            foreach (var k in overlap)
            {
                if (((ValueA(k) ^ ValueB(k)) >> lane & 1) != 0)
                {
                    conflicts.Add((k >> 3, streamBitB[k] >> 3));
                }
            }

            if (TryCover(conflicts, out var sacrificedA, out var sacrificedB))
            {
                candidates.Add((lane, sacrificedA, sacrificedB));
            }
        }

        MirrorSolution? best = null;
        foreach (var (lane, sacrificedA, sacrificedB) in candidates
                     .OrderBy(c => c.A.Count + c.B.Count)
                     .ThenBy(c => Math.Max(c.A.Count, c.B.Count))
                     .Take(MaxLanesWithCorrections))
        {
            if (best is not null && best.CorrectedTotal < sacrificedA.Count + sacrificedB.Count)
            {
                break;
            }

            var system = Build(sacrificedA, sacrificedB, out var plan);
            var reduced = system.Reduce();
            var laneBit = 1UL << lane;
            if ((reduced.InconsistentLanes & laneBit) != 0)
            {
                continue;
            }

            if (Pick(reduced, plan, laneBit) is { } found && (best is null || found.Score < best.Score))
            {
                best = found;
            }
        }

        return best;
    }

    /// <summary>
    /// Gierige Knotenüberdeckung: Jeder Konflikt verlangt, dass entweder das A-Codewort oder das B-Codewort
    /// geopfert wird. Pro Block höchstens ⌊ECC/2⌋, pro Richtung höchstens das Budget.
    /// </summary>
    private bool TryCover(List<(int A, int B)> conflicts, out HashSet<int> resultA, out HashSet<int> resultB)
    {
        HashSet<int> sacrificedA = [], sacrificedB = [];
        resultA = sacrificedA;
        resultB = sacrificedB;
        var usedA = new int[blocksA.BlockCount];
        var usedB = new int[blocksB.BlockCount];
        var remaining = conflicts.Distinct().ToList();

        while (remaining.Count > 0)
        {
            var choice = remaining
                .SelectMany(c => new[] { (Side: 0, Byte: c.A), (Side: 1, Byte: c.B) })
                .GroupBy(x => x)
                .Select(g => (g.Key.Side, g.Key.Byte, Count: g.Count()))
                .Where(x => x.Side == 0
                    ? sacrificedA.Count < settings.MaxCorrectedCodewords && usedA[blocksA.BlockOfStreamByte[x.Byte]] < blocksA.CorrectablePerBlock
                    : sacrificedB.Count < settings.MaxCorrectedCodewords && usedB[blocksB.BlockOfStreamByte[x.Byte]] < blocksB.CorrectablePerBlock)
                .OrderByDescending(x => x.Count)
                .ThenBy(x => x.Side == 0 ? sacrificedA.Count : sacrificedB.Count)
                .Select(x => ((int Side, int Byte, int Count)?)x)
                .FirstOrDefault();

            if (choice is null)
            {
                return false;
            }

            var (side, streamByte, _) = choice.Value;

            if (side == 0)
            {
                sacrificedA.Add(streamByte);
                usedA[blocksA.BlockOfStreamByte[streamByte]]++;
                remaining.RemoveAll(c => c.A == streamByte);
            }
            else
            {
                sacrificedB.Add(streamByte);
                usedB[blocksB.BlockOfStreamByte[streamByte]]++;
                remaining.RemoveAll(c => c.B == streamByte);
            }
        }

        return true;
    }

    private sealed record Plan(bool[] IsFixed, ulong[] FixedValue, int Columns);

    private Gf2System Build(HashSet<int> sacrificedA, HashSet<int> sacrificedB, out Plan plan)
    {
        var isFixed = new bool[variables];
        var fixedValue = new ulong[variables];
        for (var k = 0; k < variables; k++)
        {
            if (FixedByA(k) && !sacrificedA.Contains(k >> 3))
            {
                isFixed[k] = true;
                fixedValue[k] = ValueA(k);
            }
            else if (FixedByB(k) && !sacrificedB.Contains(streamBitB[k] >> 3))
            {
                isFixed[k] = true;
                fixedValue[k] = ValueB(k);
            }
        }

        // Geopferte Codewörter: Das „wahre“ Codewort, das der Scanner rekonstruiert, ist dort frei
        // (Hilfsspalten) bzw. gleich dem Nachrichtenbit – unabhängig vom gedruckten Modul.
        var columns = variables;
        var auxiliaryA = new Dictionary<int, int>();
        var auxiliaryB = new Dictionary<int, int>();
        foreach (var streamByte in sacrificedA)
        {
            for (var t = 0; t < 8; t++)
            {
                if (messageA[8 * streamByte + t] < 0)
                {
                    auxiliaryA[8 * streamByte + t] = columns++;
                }
            }
        }

        foreach (var streamByte in sacrificedB)
        {
            for (var t = 0; t < 8; t++)
            {
                if (messageB[8 * streamByte + t] < 0)
                {
                    auxiliaryB[8 * streamByte + t] = columns++;
                }
            }
        }

        var system = new Gf2System(columns);
        AddSyndromeRows(system, blocksA, s =>
        {
            if (sacrificedA.Contains(s >> 3))
            {
                return messageA[s] >= 0 ? (-1, messageA[s] == 1 ? AllLanes : 0) : (auxiliaryA[s], 0);
            }

            return isFixed[s] ? (-1, maskLanesA[s] ^ fixedValue[s]) : (s, maskLanesA[s]);
        });

        AddSyndromeRows(system, blocksB, s =>
        {
            if (sacrificedB.Contains(s >> 3))
            {
                return messageB[s] >= 0 ? (-1, messageB[s] == 1 ? AllLanes : 0) : (auxiliaryB[s], 0);
            }

            var k = layout.TransposedVariable[s];
            return isFixed[k] ? (-1, maskLanesB[s] ^ fixedValue[k]) : (k, maskLanesB[s]);
        });

        plan = new Plan(isFixed, fixedValue, columns);
        return system;
    }

    /// <summary>
    /// Fügt pro Block und Syndrom i die 8 Bitgleichungen Σⱼ cⱼ·α^{i(L-1-j)} = 0 hinzu.
    /// <paramref name="term"/> liefert für ein Strombit die Spalte (oder -1) und den konstanten Anteil pro Spur.
    /// </summary>
    private static void AddSyndromeRows(Gf2System system, BlockLayout blocks, Func<int, (int Column, ulong Constant)> term)
    {
        var rows = new ulong[8][];
        var rhs = new ulong[8];
        for (var block = 0; block < blocks.BlockCount; block++)
        {
            var length = blocks.BlockLength(block);
            var terms = new (int Column, ulong Constant)[length * 8];
            for (var j = 0; j < length; j++)
            {
                for (var t = 0; t < 8; t++)
                {
                    terms[j * 8 + t] = term(8 * blocks.StreamIndex[block][j] + 7 - t);
                }
            }

            for (var i = 0; i < blocks.EccPerBlock; i++)
            {
                for (var o = 0; o < 8; o++)
                {
                    rows[o] = system.CreateRow();
                    rhs[o] = 0;
                }

                for (var j = 0; j < length; j++)
                {
                    var beta = Gf256.Exp(i * (length - 1 - j));
                    for (var t = 0; t < 8; t++)
                    {
                        int product = Gf256.Multiply((byte)(1 << t), beta);
                        var (column, constant) = terms[j * 8 + t];
                        for (var o = 0; o < 8; o++)
                        {
                            if ((product >> o & 1) == 0)
                            {
                                continue;
                            }

                            if (column >= 0)
                            {
                                rows[o][column >> 6] ^= 1UL << column;
                            }

                            rhs[o] ^= constant;
                        }
                    }
                }

                for (var o = 0; o < 8; o++)
                {
                    system.Add(rows[o], rhs[o]);
                }
            }
        }
    }

    private MirrorSolution? Pick(Gf2Reduced reduced, Plan plan, ulong lanes)
    {
        MirrorSolution? best = null;
        var size = layout.Size;
        var assignment = new ulong[(plan.Columns + 63) >> 6];

        for (var remaining = lanes; remaining != 0; remaining &= remaining - 1)
        {
            var lane = BitOperations.TrailingZeroCount(remaining);
            int maskA = lane >> 3, maskB = lane & 7;
            var format = FormatPlanner.Get(blocksA.Ecc, blocksB.Ecc, maskA, maskB)!.Value;

            for (var attempt = 0; attempt < settings.Attempts; attempt++)
            {
                settings.Random.NextBytes(MemoryMarshal.AsBytes(assignment.AsSpan()));
                reduced.Solve(lane, assignment);

                var grid = (bool[])layout.FunctionValues.Clone();
                for (var k = 0; k < variables; k++)
                {
                    grid[layout.DataPositions[k]] = plan.IsFixed[k]
                        ? (plan.FixedValue[k] >> lane & 1) != 0
                        : (assignment[k >> 6] >> k & 1) != 0;
                }

                // Kopie 2 enthält auf Bit 7 das Dark Module der gespiegelten Lesart (immer 1).
                FormatInfo.Draw(grid, size, format.Copy1, copy: 1, transposed: false);
                FormatInfo.Draw(grid, size, format.Copy2, copy: 2, transposed: false);
                grid[(size - 8) * size + 8] = true;

                if (Evaluate(grid, maskA, maskB, format) is { } candidate &&
                    (best is null || candidate.Score < best.Score))
                {
                    best = candidate;
                }
            }
        }

        return best;
    }

    private MirrorSolution? Evaluate(bool[] grid, int maskA, int maskB, FormatPlan format)
    {
        var size = layout.Size;
        var matrix = new QrMatrix(size, grid);

        // Sicherheitsnetz: Jede Lösung wird in beiden Richtungen vollständig dekodiert.
        if (!QrReader.TryReadUpright(matrix, out var normal, out _) ||
            normal.Text != payloadA.Text || normal.Mask != maskA || normal.Ecc != blocksA.Ecc)
        {
            return null;
        }

        if (!QrReader.TryReadUpright(matrix.Transpose(), out var mirrored, out _) ||
            mirrored.Text != payloadB.Text || mirrored.Mask != maskB || mirrored.Ecc != blocksB.Ecc)
        {
            return null;
        }

        return new MirrorSolution(matrix, normal, mirrored, format, Penalty.Score(grid, size));
    }
}
