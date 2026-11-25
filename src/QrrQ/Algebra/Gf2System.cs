using System.Numerics;

namespace QrrQ.Algebra;

/// <summary>
/// Linear system of equations over GF(2) with bitset rows.
/// The right-hand side is a <see cref="ulong"/>: each of the 64 bits is its own "lane"
/// (here: a mask combination) that is solved along in the same elimination.
/// </summary>
internal sealed class Gf2System(int columns)
{
    private readonly List<ulong[]> rows = [];
    private readonly List<ulong> rightHandSides = [];

    public int Columns { get; } = columns;

    public int Words { get; } = (columns + 63) >> 6;

    public int Rows => rows.Count;

    public ulong[] CreateRow() => new ulong[Words];

    public void Add(ulong[] row, ulong rightHandSide)
    {
        rows.Add(row);
        rightHandSides.Add(rightHandSide);
    }

    /// <summary>Gauss-Jordan elimination to reduced row echelon form.</summary>
    public Gf2Reduced Reduce()
    {
        var matrix = rows.ToArray();
        var rhs = rightHandSides.ToArray();
        List<int> pivots = [with(capacity: Math.Min(matrix.Length, Columns))];
        var rank = 0;

        for (var column = 0; column < Columns && rank < matrix.Length; column++)
        {
            var word = column >> 6;
            var bit = 1UL << column;
            var selected = -1;
            for (var r = rank; r < matrix.Length; r++)
            {
                if ((matrix[r][word] & bit) != 0)
                {
                    selected = r;
                    break;
                }
            }

            if (selected < 0)
            {
                continue;
            }

            (matrix[rank], matrix[selected]) = (matrix[selected], matrix[rank]);
            (rhs[rank], rhs[selected]) = (rhs[selected], rhs[rank]);

            var pivotRow = matrix[rank];
            var pivotRhs = rhs[rank];
            for (var r = 0; r < matrix.Length; r++)
            {
                if (r != rank && (matrix[r][word] & bit) != 0)
                {
                    XorInto(matrix[r].AsSpan(word), pivotRow.AsSpan(word));
                    rhs[r] ^= pivotRhs;
                }
            }

            pivots.Add(column);
            rank++;
        }

        var inconsistent = 0UL;
        for (var r = rank; r < matrix.Length; r++)
        {
            inconsistent |= rhs[r];
        }

        return new Gf2Reduced(matrix[..rank], rhs[..rank], [.. pivots], inconsistent);
    }

    private static void XorInto(Span<ulong> target, ReadOnlySpan<ulong> source)
    {
        var i = 0;
        if (Vector.IsHardwareAccelerated)
        {
            var width = Vector<ulong>.Count;
            for (; i <= target.Length - width; i += width)
            {
                (new Vector<ulong>(target[i..]) ^ new Vector<ulong>(source[i..])).CopyTo(target[i..]);
            }
        }

        for (; i < target.Length; i++)
        {
            target[i] ^= source[i];
        }
    }
}

/// <summary>A reduced system from which solutions can be drawn for every lane.</summary>
internal sealed class Gf2Reduced(ulong[][] rows, ulong[] rightHandSides, int[] pivots, ulong inconsistentLanes)
{
    /// <summary>Lanes for which the system is inconsistent.</summary>
    public ulong InconsistentLanes { get; } = inconsistentLanes;

    public int Rank => pivots.Length;

    /// <summary>
    /// Completes an assignment: free variables keep their value from <paramref name="assignment"/>,
    /// pivot variables are set so that all equations of lane <paramref name="lane"/> hold.
    /// </summary>
    public void Solve(int lane, Span<ulong> assignment)
    {
        foreach (var pivot in pivots)
        {
            assignment[pivot >> 6] &= ~(1UL << pivot);
        }

        for (var r = 0; r < pivots.Length; r++)
        {
            var row = rows[r];
            var parity = (int)(rightHandSides[r] >> lane & 1);
            for (var word = pivots[r] >> 6; word < row.Length; word++)
            {
                parity ^= BitOperations.PopCount(row[word] & assignment[word]) & 1;
            }

            if (parity != 0)
            {
                assignment[pivots[r] >> 6] |= 1UL << pivots[r];
            }
        }
    }
}
