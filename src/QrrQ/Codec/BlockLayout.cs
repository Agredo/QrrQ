namespace QrrQ.Codec;

/// <summary>
/// Aufteilung der Codewörter auf Reed-Solomon-Blöcke und deren Verschränkung (Interleaving)
/// im Bitstrom für eine Kombination aus Version und Fehlerkorrekturstufe.
/// </summary>
internal sealed class BlockLayout
{
    private static readonly BlockLayout?[] Cache = new BlockLayout?[(QrTables.MaxVersion + 1) * 4];

    private BlockLayout(int version, EccLevel ecc)
    {
        Version = version;
        Ecc = ecc;
        BlockCount = QrTables.BlockCount(version, ecc);
        EccPerBlock = QrTables.EccPerBlock(version, ecc);
        TotalCodewords = QrTables.TotalCodewords(version);
        ShortBlocks = BlockCount - TotalCodewords % BlockCount;
        ShortBlockLength = TotalCodewords / BlockCount;
        DataCodewords = TotalCodewords - BlockCount * EccPerBlock;

        StreamIndex = new int[BlockCount][];
        for (var block = 0; block < BlockCount; block++)
        {
            StreamIndex[block] = new int[BlockLength(block)];
        }

        // Daten: Spalte für Spalte über alle Blöcke, danach die ECC-Codewörter genauso.
        var next = 0;
        var maxDataLength = DataLength(BlockCount - 1);
        for (var i = 0; i < maxDataLength; i++)
        {
            for (var block = 0; block < BlockCount; block++)
            {
                if (i < DataLength(block))
                {
                    StreamIndex[block][i] = next++;
                }
            }
        }

        for (var i = 0; i < EccPerBlock; i++)
        {
            for (var block = 0; block < BlockCount; block++)
            {
                StreamIndex[block][DataLength(block) + i] = next++;
            }
        }

        if (next != TotalCodewords)
        {
            throw new InvalidOperationException("Inkonsistente Blocktabelle.");
        }

        DataStreamIndex = new int[DataCodewords];
        BlockOfStreamByte = new int[TotalCodewords];
        var dataIndex = 0;
        for (var block = 0; block < BlockCount; block++)
        {
            for (var i = 0; i < BlockLength(block); i++)
            {
                BlockOfStreamByte[StreamIndex[block][i]] = block;
                if (i < DataLength(block))
                {
                    DataStreamIndex[dataIndex++] = StreamIndex[block][i];
                }
            }
        }
    }

    public int Version { get; }

    public EccLevel Ecc { get; }

    public int BlockCount { get; }

    public int EccPerBlock { get; }

    public int TotalCodewords { get; }

    public int DataCodewords { get; }

    public int ShortBlocks { get; }

    public int ShortBlockLength { get; }

    /// <summary>[Block][Index im Block] → Position im verschränkten Codewortstrom.</summary>
    public int[][] StreamIndex { get; }

    /// <summary>Datencodewort in Lesereihenfolge (Block 0, Block 1, …) → Position im Strom.</summary>
    public int[] DataStreamIndex { get; }

    /// <summary>Position im Strom → Blocknummer.</summary>
    public int[] BlockOfStreamByte { get; }

    /// <summary>Wie viele Codewörter pro Block maximal korrigiert werden können.</summary>
    public int CorrectablePerBlock => EccPerBlock / 2;

    public static BlockLayout Get(int version, EccLevel ecc) =>
        Cache[version * 4 + (int)ecc] ??= new BlockLayout(version, ecc);

    public int BlockLength(int block) => ShortBlockLength + (block < ShortBlocks ? 0 : 1);

    public int DataLength(int block) => BlockLength(block) - EccPerBlock;
}
