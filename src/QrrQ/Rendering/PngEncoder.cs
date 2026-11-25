using System.Buffers.Binary;
using System.IO.Compression;

namespace QrrQ.Rendering;

/// <summary>Minimal PNG encoder (1-bit grayscale) without external dependencies.</summary>
internal static class PngEncoder
{
    private static readonly uint[] CrcTable = BuildCrcTable();

    public static byte[] Encode(QrMatrix matrix, int moduleSize, int quietZone)
    {
        var modulesPerSide = matrix.Size + 2 * quietZone;
        var pixels = modulesPerSide * moduleSize;
        var stride = (pixels + 7) / 8;
        var raw = new byte[(stride + 1) * pixels];

        for (var y = 0; y < pixels; y++)
        {
            var rowStart = y * (stride + 1); // raw[rowStart] = 0: filter type "None"
            var moduleRow = y / moduleSize - quietZone;
            for (var x = 0; x < pixels; x++)
            {
                var moduleColumn = x / moduleSize - quietZone;
                var dark = moduleRow >= 0 && moduleRow < matrix.Size &&
                           moduleColumn >= 0 && moduleColumn < matrix.Size &&
                           matrix[moduleRow, moduleColumn];
                if (!dark)
                {
                    raw[rowStart + 1 + (x >> 3)] |= (byte)(0x80 >> (x & 7)); // 1 = white
                }
            }
        }

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, pixels);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], pixels);
        header[8] = 1;  // bit depth
        header[9] = 0;  // grayscale
        header[10] = 0; // deflate
        header[11] = 0; // standard filter
        header[12] = 0; // no interlacing
        WriteChunk(png, "IHDR"u8, header);
        WriteChunk(png, "IDAT"u8, Compress(raw));
        WriteChunk(png, "IEND"u8, []);
        return png.ToArray();
    }

    private static byte[] Compress(byte[] raw)
    {
        using var output = new MemoryStream();
        using (var zlib = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            zlib.Write(raw);
        }

        return output.ToArray();
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> number = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(number, data.Length);
        stream.Write(number);
        stream.Write(type);
        stream.Write(data);

        var crc = Crc(0xFFFFFFFFu, type);
        crc = Crc(crc, data) ^ 0xFFFFFFFFu;
        BinaryPrimitives.WriteUInt32BigEndian(number, crc);
        stream.Write(number);
    }

    private static uint Crc(uint crc, ReadOnlySpan<byte> data)
    {
        foreach (var b in data)
        {
            crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        }

        return crc;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
