namespace QrrQ;

/// <summary>
/// Immutable, square module grid of a QR code.
/// <c>true</c> stands for a dark module, indices are [row, column].
/// </summary>
public sealed class QrMatrix : IEquatable<QrMatrix>
{
    private readonly bool[] modules;

    /// <summary>Creates a grid from a two-dimensional array [row, column].</summary>
    public QrMatrix(bool[,] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var size = modules.GetLength(0);
        if (size != modules.GetLength(1))
        {
            throw new ArgumentException("The grid must be square.", nameof(modules));
        }

        Size = size;
        this.modules = new bool[size * size];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                this.modules[row * size + column] = modules[row, column];
            }
        }
    }

    /// <summary>Takes over a row-major grid without copying.</summary>
    internal QrMatrix(int size, bool[] modules)
    {
        if (modules.Length != size * size)
        {
            throw new ArgumentException("Grid does not match the size.", nameof(modules));
        }

        Size = size;
        this.modules = modules;
    }

    /// <summary>Edge length in modules (21 for version 1 ... 177 for version 40).</summary>
    public int Size { get; }

    /// <summary>Returns whether the module at [row, column] is dark.</summary>
    public bool this[int row, int column] => modules[row * Size + column];

    internal ReadOnlySpan<bool> Modules => modules;

    /// <summary>Mirrors along the main diagonal (rows become columns).</summary>
    public QrMatrix Transpose() => Map(static (size, row, column) => column * size + row);

    /// <summary>Mirrors left/right - the way a mirror shows the code.</summary>
    public QrMatrix FlipHorizontal() => Map(static (size, row, column) => row * size + (size - 1 - column));

    /// <summary>Mirrors top/bottom.</summary>
    public QrMatrix FlipVertical() => Map(static (size, row, column) => (size - 1 - row) * size + column);

    /// <summary>Rotates 90 degrees clockwise.</summary>
    public QrMatrix RotateClockwise() => Map(static (size, row, column) => (size - 1 - column) * size + row);

    /// <summary>Copies the grid into a two-dimensional array [row, column].</summary>
    public bool[,] ToArray()
    {
        var result = new bool[Size, Size];
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                result[row, column] = this[row, column];
            }
        }

        return result;
    }

    /// <summary>
    /// Packs the grid row by row into bytes (MSB first, 1 = dark).
    /// Every row starts at a byte boundary and occupies <c>(Size + 7) / 8</c> bytes.
    /// </summary>
    public byte[] ToPackedRows()
    {
        var stride = (Size + 7) / 8;
        var result = new byte[stride * Size];
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                if (this[row, column])
                {
                    result[row * stride + (column >> 3)] |= (byte)(0x80 >> (column & 7));
                }
            }
        }

        return result;
    }

    /// <inheritdoc />
    public bool Equals(QrMatrix? other) =>
        other is not null && other.Size == Size && other.modules.AsSpan().SequenceEqual(modules);

    /// <inheritdoc />
    public override bool Equals(object? obj) => Equals(obj as QrMatrix);

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Size);
        foreach (var module in modules)
        {
            hash.Add(module);
        }

        return hash.ToHashCode();
    }

    private QrMatrix Map(Func<int, int, int, int> sourceIndex)
    {
        var result = new bool[modules.Length];
        for (var row = 0; row < Size; row++)
        {
            for (var column = 0; column < Size; column++)
            {
                result[row * Size + column] = modules[sourceIndex(Size, row, column)];
            }
        }

        return new QrMatrix(Size, result);
    }
}
