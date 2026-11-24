namespace QrrQ.Codec;

/// <summary>Tabellen aus ISO/IEC 18004 (Blockstruktur, Codewortanzahlen, Ausrichtungsmuster).</summary>
internal static class QrTables
{
    public const int MinVersion = 1;
    public const int MaxVersion = 40;

    /// <summary>ECC-Codewörter pro Block, Index [stufe * 41 + version], Stufenreihenfolge L, M, Q, H.</summary>
    private static ReadOnlySpan<byte> EccCodewordsPerBlock =>
    [
        // L
        0, 7, 10, 15, 20, 26, 18, 20, 24, 30, 18, 20, 24, 26, 30, 22, 24, 28, 30, 28, 28,
        28, 28, 30, 30, 26, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30,
        // M
        0, 10, 16, 26, 18, 24, 16, 18, 22, 22, 26, 30, 22, 22, 24, 24, 28, 28, 26, 26, 26,
        26, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28, 28,
        // Q
        0, 13, 22, 18, 26, 18, 24, 18, 22, 20, 24, 28, 26, 24, 20, 30, 24, 28, 28, 26, 30,
        28, 30, 30, 30, 30, 28, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30,
        // H
        0, 17, 28, 22, 16, 22, 28, 26, 26, 24, 28, 24, 28, 22, 24, 24, 30, 28, 28, 26, 28,
        30, 24, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30, 30,
    ];

    /// <summary>Anzahl Fehlerkorrekturblöcke, Index [stufe * 41 + version].</summary>
    private static ReadOnlySpan<byte> ErrorCorrectionBlocks =>
    [
        // L
        0, 1, 1, 1, 1, 1, 2, 2, 2, 2, 4, 4, 4, 4, 4, 6, 6, 6, 6, 7, 8,
        8, 9, 9, 10, 12, 12, 12, 13, 14, 15, 16, 17, 18, 19, 19, 20, 21, 22, 24, 25,
        // M
        0, 1, 1, 1, 2, 2, 4, 4, 4, 5, 5, 5, 8, 9, 9, 10, 10, 11, 13, 14, 16,
        17, 17, 18, 20, 21, 23, 25, 26, 28, 29, 31, 33, 35, 37, 38, 40, 43, 45, 47, 49,
        // Q
        0, 1, 1, 2, 2, 4, 4, 6, 6, 8, 8, 8, 10, 12, 16, 12, 17, 16, 18, 21, 20,
        23, 23, 25, 27, 29, 34, 34, 35, 38, 40, 43, 45, 48, 51, 53, 56, 59, 62, 65, 68,
        // H
        0, 1, 1, 2, 4, 4, 4, 5, 6, 8, 8, 11, 11, 16, 16, 18, 16, 19, 21, 25, 25,
        25, 34, 30, 32, 35, 37, 40, 42, 45, 48, 51, 54, 57, 60, 63, 66, 70, 74, 77, 81,
    ];

    public static int EccPerBlock(int version, EccLevel ecc) => EccCodewordsPerBlock[(int)ecc * 41 + version];

    public static int BlockCount(int version, EccLevel ecc) => ErrorCorrectionBlocks[(int)ecc * 41 + version];

    /// <summary>Module, die nach Abzug aller Funktionsmuster für Daten übrig bleiben (inkl. Restbits).</summary>
    public static int RawDataModules(int version)
    {
        var result = (16 * version + 128) * version + 64;
        if (version >= 2)
        {
            var alignmentCount = version / 7 + 2;
            result -= (25 * alignmentCount - 10) * alignmentCount - 55;
            if (version >= 7)
            {
                result -= 36;
            }
        }

        return result;
    }

    public static int TotalCodewords(int version) => RawDataModules(version) / 8;

    /// <summary>Zeilen-/Spaltenkoordinaten der Ausrichtungsmuster-Mittelpunkte.</summary>
    public static int[] AlignmentPositions(int version)
    {
        if (version == 1)
        {
            return [];
        }

        var count = version / 7 + 2;
        var size = version * 4 + 17;
        var step = (version * 8 + count * 3 + 5) / (count * 4 - 4) * 2;
        var result = new int[count];
        result[0] = 6;
        for (int i = count - 1, position = size - 7; i >= 1; i--, position -= step)
        {
            result[i] = position;
        }

        return result;
    }
}
