using System.Diagnostics.CodeAnalysis;
using QrrQ.Algebra;
using QrrQ.Codec;

namespace QrrQ.Reading;

/// <summary>Result of a read operation.</summary>
/// <param name="Text">Decoded content.</param>
/// <param name="Version">QR version 1-40.</param>
/// <param name="Ecc">Error correction level from the format information.</param>
/// <param name="Mask">Data mask 0-7.</param>
/// <param name="FormatDistance">Hamming distance of the best format copy (0 = exact).</param>
/// <param name="CorrectedCodewords">Codewords that had to be repaired by Reed-Solomon.</param>
public sealed record QrReadResult(string Text, int Version, EccLevel Ecc, int Mask, int FormatDistance, int CorrectedCodewords);

/// <summary>
/// Decodes a <see cref="QrMatrix"/> grid like a scanner after image processing:
/// normalize the orientation, read the format, unmask, correct with Reed-Solomon, parse segments.
/// Mirroring is deliberately <b>not</b> done automatically - just like with scanners whose normal reading path succeeds.
/// </summary>
public static class QrReader
{
    /// <summary>Reads the grid or throws a <see cref="FormatException"/>.</summary>
    public static QrReadResult Read(QrMatrix matrix) =>
        TryRead(matrix, out var result, out var error) ? result : throw new FormatException(error);

    /// <summary>Tries to read the grid. Rotations by 90/180/270 degrees are detected.</summary>
    public static bool TryRead(QrMatrix matrix, [NotNullWhen(true)] out QrReadResult? result, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (!TryNormalizeOrientation(matrix, out var upright))
        {
            result = null;
            error = "The finder patterns do not yield an unambiguous orientation.";
            return false;
        }

        return TryReadUpright(upright, out result, out error);
    }

    internal static bool TryReadUpright(QrMatrix matrix, [NotNullWhen(true)] out QrReadResult? result, [NotNullWhen(false)] out string? error)
    {
        result = null;
        var size = matrix.Size;
        if (size < 21 || size > 177 || (size - 17) % 4 != 0)
        {
            error = $"Invalid symbol size {size}.";
            return false;
        }

        var version = (size - 17) / 4;
        var modules = matrix.Modules;
        var copy1 = FormatInfo.Read(modules, size, copy: 1);
        var copy2 = FormatInfo.Read(modules, size, copy: 2);
        if (FormatInfo.Decode(copy1, copy2) is not { } format)
        {
            error = "Format information not readable.";
            return false;
        }

        var layout = SymbolLayout.Get(version);
        var blocks = BlockLayout.Get(version, format.Ecc);
        var stream = new byte[blocks.TotalCodewords];
        for (var bit = 0; bit < stream.Length * 8; bit++)
        {
            var cell = layout.DataPositions[bit];
            if (modules[cell] ^ Masks.IsDark(format.Mask, cell / size, cell % size))
            {
                stream[bit >> 3] |= (byte)(0x80 >> (bit & 7));
            }
        }

        var data = new byte[blocks.DataCodewords];
        var dataOffset = 0;
        var corrected = 0;
        for (var block = 0; block < blocks.BlockCount; block++)
        {
            var codeword = new byte[blocks.BlockLength(block)];
            for (var i = 0; i < codeword.Length; i++)
            {
                codeword[i] = stream[blocks.StreamIndex[block][i]];
            }

            var fixedCount = ReedSolomon.Correct(codeword, blocks.EccPerBlock);
            if (fixedCount < 0)
            {
                error = $"Reed-Solomon block {block} is not correctable.";
                return false;
            }

            corrected += fixedCount;
            codeword.AsSpan(0, blocks.DataLength(block)).CopyTo(data.AsSpan(dataOffset));
            dataOffset += blocks.DataLength(block);
        }

        if (!SegmentParser.TryParse(data, version, out var text, out error))
        {
            return false;
        }

        result = new QrReadResult(text, version, format.Ecc, format.Mask, format.Distance, corrected);
        error = null;
        return true;
    }

    private static bool TryNormalizeOrientation(QrMatrix matrix, out QrMatrix upright)
    {
        upright = matrix;
        var size = matrix.Size;
        if (size < 21)
        {
            return false;
        }

        var topLeft = HasFinder(matrix, 0, 0);
        var topRight = HasFinder(matrix, 0, size - 7);
        var bottomLeft = HasFinder(matrix, size - 7, 0);
        var bottomRight = HasFinder(matrix, size - 7, size - 7);

        var turns = (topLeft, topRight, bottomLeft, bottomRight) switch
        {
            (true, true, true, false) => 0,
            (true, false, true, true) => 1, // corner without a finder at the top right -> rotate once clockwise
            (false, true, true, true) => 2,
            (true, true, false, true) => 3,
            _ => -1,
        };

        for (var i = 0; i < turns; i++)
        {
            upright = upright.RotateClockwise();
        }

        return turns >= 0;
    }

    private static bool HasFinder(QrMatrix matrix, int top, int left)
    {
        for (var dr = 0; dr < 7; dr++)
        {
            for (var dc = 0; dc < 7; dc++)
            {
                var distance = Math.Max(Math.Abs(dr - 3), Math.Abs(dc - 3));
                if (matrix[top + dr, left + dc] != (distance != 2))
                {
                    return false;
                }
            }
        }

        return true;
    }
}
