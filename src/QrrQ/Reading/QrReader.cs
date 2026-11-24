using System.Diagnostics.CodeAnalysis;
using QrrQ.Algebra;
using QrrQ.Codec;

namespace QrrQ.Reading;

/// <summary>Ergebnis eines Lesevorgangs.</summary>
/// <param name="Text">Dekodierter Inhalt.</param>
/// <param name="Version">QR-Version 1–40.</param>
/// <param name="Ecc">Fehlerkorrekturstufe laut Formatinformation.</param>
/// <param name="Mask">Datenmaske 0–7.</param>
/// <param name="FormatDistance">Hamming-Abstand der besten Formatkopie (0 = exakt).</param>
/// <param name="CorrectedCodewords">Codewörter, die per Reed-Solomon repariert werden mussten.</param>
public sealed record QrReadResult(string Text, int Version, EccLevel Ecc, int Mask, int FormatDistance, int CorrectedCodewords);

/// <summary>
/// Dekodiert ein <see cref="QrMatrix"/>-Raster wie ein Scanner nach der Bildverarbeitung:
/// Lage normalisieren, Format lesen, demaskieren, Reed-Solomon korrigieren, Segmente parsen.
/// Gespiegelt wird bewusst <b>nicht</b> automatisch – genau wie bei Scannern, deren normaler Leseweg gelingt.
/// </summary>
public static class QrReader
{
    /// <summary>Liest das Raster oder wirft eine <see cref="FormatException"/>.</summary>
    public static QrReadResult Read(QrMatrix matrix) =>
        TryRead(matrix, out var result, out var error) ? result : throw new FormatException(error);

    /// <summary>Versucht, das Raster zu lesen. Drehungen um 90°/180°/270° werden erkannt.</summary>
    public static bool TryRead(QrMatrix matrix, [NotNullWhen(true)] out QrReadResult? result, [NotNullWhen(false)] out string? error)
    {
        ArgumentNullException.ThrowIfNull(matrix);
        if (!TryNormalizeOrientation(matrix, out var upright))
        {
            result = null;
            error = "Die Finder-Muster ergeben keine eindeutige Lage.";
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
            error = $"Ungültige Symbolgröße {size}.";
            return false;
        }

        var version = (size - 17) / 4;
        var modules = matrix.Modules;
        var copy1 = FormatInfo.Read(modules, size, copy: 1);
        var copy2 = FormatInfo.Read(modules, size, copy: 2);
        if (FormatInfo.Decode(copy1, copy2) is not { } format)
        {
            error = "Formatinformation nicht lesbar.";
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
                error = $"Reed-Solomon-Block {block} ist nicht korrigierbar.";
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
            (true, false, true, true) => 1, // Ecke ohne Finder oben rechts → einmal im Uhrzeigersinn
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
