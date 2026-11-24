namespace QrrQ;

/// <summary>
/// Unveränderliches, quadratisches Modulraster eines QR-Codes.
/// <c>true</c> steht für ein dunkles Modul, Indizes sind [Zeile, Spalte].
/// </summary>
public sealed class QrMatrix : IEquatable<QrMatrix>
{
    private readonly bool[] modules;

    /// <summary>Erzeugt ein Raster aus einem zweidimensionalen Array [Zeile, Spalte].</summary>
    public QrMatrix(bool[,] modules)
    {
        ArgumentNullException.ThrowIfNull(modules);
        var size = modules.GetLength(0);
        if (size != modules.GetLength(1))
        {
            throw new ArgumentException("Das Raster muss quadratisch sein.", nameof(modules));
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

    /// <summary>Übernimmt ein zeilenweise abgelegtes Raster ohne Kopie.</summary>
    internal QrMatrix(int size, bool[] modules)
    {
        if (modules.Length != size * size)
        {
            throw new ArgumentException("Raster passt nicht zur Größe.", nameof(modules));
        }

        Size = size;
        this.modules = modules;
    }

    /// <summary>Kantenlänge in Modulen (21 für Version 1 … 177 für Version 40).</summary>
    public int Size { get; }

    /// <summary>Liefert, ob das Modul an [Zeile, Spalte] dunkel ist.</summary>
    public bool this[int row, int column] => modules[row * Size + column];

    internal ReadOnlySpan<bool> Modules => modules;

    /// <summary>Spiegelt an der Hauptdiagonalen (Zeilen werden Spalten).</summary>
    public QrMatrix Transpose() => Map(static (size, row, column) => column * size + row);

    /// <summary>Spiegelt links/rechts – so, wie ein Spiegel den Code zeigt.</summary>
    public QrMatrix FlipHorizontal() => Map(static (size, row, column) => row * size + (size - 1 - column));

    /// <summary>Spiegelt oben/unten.</summary>
    public QrMatrix FlipVertical() => Map(static (size, row, column) => (size - 1 - row) * size + column);

    /// <summary>Dreht um 90° im Uhrzeigersinn.</summary>
    public QrMatrix RotateClockwise() => Map(static (size, row, column) => (size - 1 - column) * size + row);

    /// <summary>Kopiert das Raster in ein zweidimensionales Array [Zeile, Spalte].</summary>
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
    /// Packt das Raster zeilenweise in Bytes (MSB zuerst, 1 = dunkel).
    /// Jede Zeile beginnt an einer Bytegrenze und belegt <c>(Size + 7) / 8</c> Bytes.
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
