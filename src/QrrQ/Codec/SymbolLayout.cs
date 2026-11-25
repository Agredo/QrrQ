namespace QrrQ.Codec;

/// <summary>
/// Geometry of a QR version: function patterns, zigzag order of the data modules
/// and the permutation that the transposition causes on the data modules.
/// </summary>
/// <remarks>
/// All function patterns (finder, timing, alignment, format and version area, dark module)
/// map onto themselves under transposition. The data modules are therefore only permuted -
/// which is exactly what turns the mirror trick into a linear problem over GF(2).
/// </remarks>
internal sealed class SymbolLayout
{
    private static readonly SymbolLayout?[] Cache = new SymbolLayout?[QrTables.MaxVersion + 1];

    private SymbolLayout(int version)
    {
        Version = version;
        Size = version * 4 + 17;
        var size = Size;
        IsFunction = new bool[size * size];
        FunctionValues = new bool[size * size];

        for (var i = 0; i < size; i++)
        {
            SetFunction(6, i, i % 2 == 0);
            SetFunction(i, 6, i % 2 == 0);
        }

        DrawFinder(3, 3);
        DrawFinder(3, size - 4);
        DrawFinder(size - 4, 3);

        var alignment = QrTables.AlignmentPositions(version);
        var last = alignment.Length - 1;
        for (var i = 0; i < alignment.Length; i++)
        {
            for (var j = 0; j < alignment.Length; j++)
            {
                if ((i == 0 && j == 0) || (i == 0 && j == last) || (i == last && j == 0))
                {
                    continue;
                }

                DrawAlignment(alignment[i], alignment[j]);
            }
        }

        // Reserve the format area (the bits are drawn later per solution).
        for (var bit = 0; bit < 15; bit++)
        {
            var (row1, column1) = FormatInfo.Copy1Cell(bit);
            IsFunction[row1 * size + column1] = true;
            var (row2, column2) = FormatInfo.Copy2Cell(size, bit);
            IsFunction[row2 * size + column2] = true;
        }

        SetFunction(size - 8, 8, true); // Dark Module

        if (version >= 7)
        {
            var bits = FormatInfo.VersionBits(version);
            for (var i = 0; i < 18; i++)
            {
                var dark = (bits >> i & 1) != 0;
                SetFunction(i / 3, size - 11 + i % 3, dark);
                SetFunction(size - 11 + i % 3, i / 3, dark);
            }
        }

        List<int> positions = [with(capacity: size * size)];
        for (var right = size - 1; right >= 1; right -= 2)
        {
            if (right == 6)
            {
                right = 5;
            }

            var upward = ((right + 1) & 2) == 0;
            for (var vertical = 0; vertical < size; vertical++)
            {
                var row = upward ? size - 1 - vertical : vertical;
                for (var j = 0; j < 2; j++)
                {
                    var cell = row * size + right - j;
                    if (!IsFunction[cell])
                    {
                        positions.Add(cell);
                    }
                }
            }
        }

        DataPositions = [.. positions];
        if (DataPositions.Length != QrTables.RawDataModules(version))
        {
            throw new InvalidOperationException($"Version {version}: wrong number of data modules.");
        }

        VariableAt = new int[size * size];
        Array.Fill(VariableAt, -1);
        for (var k = 0; k < DataPositions.Length; k++)
        {
            VariableAt[DataPositions[k]] = k;
        }

        TransposedVariable = new int[DataPositions.Length];
        for (var k = 0; k < DataPositions.Length; k++)
        {
            var cell = DataPositions[k];
            var transposed = VariableAt[cell % size * size + cell / size];
            TransposedVariable[k] = transposed >= 0
                ? transposed
                : throw new InvalidOperationException("Data area is not symmetric under transposition.");
        }
    }

    public int Version { get; }

    public int Size { get; }

    /// <summary>Cell (row * Size + column) belongs to a function pattern.</summary>
    public bool[] IsFunction { get; }

    /// <summary>Fixed values of the function patterns; format bits are still light here.</summary>
    public bool[] FunctionValues { get; }

    /// <summary>Data modules in zigzag order: stream bit k lives in cell DataPositions[k].</summary>
    public int[] DataPositions { get; }

    /// <summary>Cell -> index in the data stream, -1 for function modules.</summary>
    public int[] VariableAt { get; }

    /// <summary>σ: stream bit k of the transposed code lives on data module σ(k) in the original.</summary>
    public int[] TransposedVariable { get; }

    public static SymbolLayout Get(int version)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(version, QrTables.MinVersion);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(version, QrTables.MaxVersion);
        return Cache[version] ??= new SymbolLayout(version);
    }

    private void SetFunction(int row, int column, bool dark)
    {
        IsFunction[row * Size + column] = true;
        FunctionValues[row * Size + column] = dark;
    }

    private void DrawFinder(int centerRow, int centerColumn)
    {
        for (var dr = -4; dr <= 4; dr++)
        {
            for (var dc = -4; dc <= 4; dc++)
            {
                var row = centerRow + dr;
                var column = centerColumn + dc;
                if (row >= 0 && row < Size && column >= 0 && column < Size)
                {
                    var distance = Math.Max(Math.Abs(dr), Math.Abs(dc));
                    SetFunction(row, column, distance is not 2 and not 4);
                }
            }
        }
    }

    private void DrawAlignment(int centerRow, int centerColumn)
    {
        for (var dr = -2; dr <= 2; dr++)
        {
            for (var dc = -2; dc <= 2; dc++)
            {
                SetFunction(centerRow + dr, centerColumn + dc, Math.Max(Math.Abs(dr), Math.Abs(dc)) != 1);
            }
        }
    }
}
