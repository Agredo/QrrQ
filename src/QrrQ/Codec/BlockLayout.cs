namespace QrrQ.Codec;

/// <summary>
/// Distribution of the codewords over Reed-Solomon blocks and their interleaving
/// in the bit stream for one version and error correction level combination.
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

        // Data: column by column over all blocks, then the ECC codewords the same way.
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
            throw new InvalidOperationException("Inconsistent block table.");
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

    /// <summary>[block][index in block] -> position in the interleaved codeword stream.</summary>
    public int[][] StreamIndex { get; }

    /// <summary>Data codeword in reading order (block 0, block 1, ...) -> position in the stream.</summary>
    public int[] DataStreamIndex { get; }

    /// <summary>Position in the stream -> block number.</summary>
    public int[] BlockOfStreamByte { get; }

    /// <summary>How many codewords per block can be corrected at most.</summary>
    public int CorrectablePerBlock => EccPerBlock / 2;

    public static BlockLayout Get(int version, EccLevel ecc) =>
        Cache[version * 4 + (int)ecc] ??= new BlockLayout(version, ecc);

    public int BlockLength(int block) => ShortBlockLength + (block < ShortBlocks ? 0 : 1);

    public int DataLength(int block) => BlockLength(block) - EccPerBlock;
}
