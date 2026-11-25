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

    /// <summary>Smaller is better: first data error-freeness, then exact format info, then visual appearance.</summary>
    public long Score => CorrectedTotal * 1_000_000L + Format.Cost * 10_000L + Penalty;
}

/// <summary>
/// Finds a module grid M that reads as text A when upright and as text B when transposed
/// (= mirrored and rotated).
/// </summary>
/// <remarks>
/// <para>
/// The unknowns are the data modules x₀ … x_{D-1} of M (zigzag order). For direction A stream bit
/// k = x_k ⊕ maskA(k); for direction B stream bit k = x_{σ(k)} ⊕ maskB(k), where σ is the transposition
/// on the data modules. Both are affine in x.
/// </para>
/// <para>
/// Constraints: (1) the message bits of both texts (they fix individual x), (2) all Reed-Solomon syndromes
/// of both directions are 0. Because multiplying by a constant in GF(256) is GF(2)-linear, every syndrome
/// splits into 8 bit equations. The degrees of freedom come from the pad bytes after the terminator, which
/// scanners ignore.
/// </para>
/// <para>
/// The right-hand side depends on the masks only through constants. All 8 × 8 mask combinations are therefore
/// solved as 64 lanes of a <see cref="ulong"/> in <b>one</b> elimination.
/// </para>
/// <para>
/// A small overlap is unavoidable: both messages start in the bottom right, and that corner maps onto itself
/// under the transposition. There the bits of both texts must agree - which is what the mask choice governs.
/// If that does not work out, one codeword per conflict of one side may (optionally) be sacrificed; the scanner
/// then repairs it through error correction.
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

        // Lane = 8 · maskA + maskB. Bit "lane" set <=> the mask is dark at this module.
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

        // Only mask pairs for which a scanner-proof format assignment exists.
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

    /// <summary>Rough pre-check: do both texts fit and do enough degrees of freedom remain?</summary>
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

    /// <summary>Does this version have room for two payloads at all (regardless of the texts)?</summary>
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

    /// <summary>Lanes in which a module used by both messages would need two different values.</summary>
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

    /// <summary>Module value x_k (per lane) with which direction A sees its message bit.</summary>
    private ulong ValueA(int k) => (messageA[k] == 1 ? AllLanes : 0) ^ maskLanesA[k];

    /// <summary>Module value x_k (per lane) with which direction B sees its message bit.</summary>
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

        // Short encodings first; the single-segment encoding of both texts is always the first pair.
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
    /// Greedy vertex cover: every conflict requires that either the A codeword or the B codeword
    /// is sacrificed. At most ⌊ECC/2⌋ per block, at most the budget per direction.
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

        // Sacrificed codewords: the "true" codeword that the scanner reconstructs is free there
        // (auxiliary columns) or equal to the message bit - independent of the printed module.
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
    /// Adds the 8 bit equations Σⱼ cⱼ·α^{i(L-1-j)} = 0 per block and syndrome i.
    /// <paramref name="term"/> returns the column (or -1) and the constant part per lane for a stream bit.
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

                // Copy 2 carries the dark module of the mirrored reading on bit 7 (always 1).
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

        // Safety net: every solution is fully decoded in both directions.
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
