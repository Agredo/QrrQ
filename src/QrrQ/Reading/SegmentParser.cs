using System.Diagnostics.CodeAnalysis;
using System.Text;
using System.Text.Unicode;

namespace QrrQ.Reading;

/// <summary>Splits the data bit stream into segments (numeric, alphanumeric, byte, ECI).</summary>
internal static class SegmentParser
{
    private const string AlphanumericCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";

    private static readonly Encoding StrictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static bool TryParse(ReadOnlySpan<byte> data, int version, out string text, [NotNullWhen(false)] out string? error)
    {
        var reader = new BitReader(data);
        var builder = new StringBuilder();
        Encoding? eciEncoding = null;
        text = string.Empty;
        error = null;

        parse: while (reader.Remaining >= 4)
        {
            var mode = reader.Read(4);
            switch (mode)
            {
                case 0b0000:
                    break parse; // Terminator - everything after it are pad bits.

                case 0b0001:
                    {
                        var count = reader.Read(version <= 9 ? 10 : version <= 26 ? 12 : 14);
                        for (; count >= 3; count -= 3)
                        {
                            var value = reader.Read(10);
                            if (value > 999) return Fail("Invalid digit group.", out error);
                            builder.Append(value.ToString("D3"));
                        }

                        if (count > 0)
                        {
                            var value = reader.Read(count == 2 ? 7 : 4);
                            if (value >= (count == 2 ? 100 : 10)) return Fail("Invalid digit group.", out error);
                            builder.Append(value.ToString(count == 2 ? "D2" : "D1"));
                        }

                        break;
                    }

                case 0b0010:
                    {
                        var count = reader.Read(version <= 9 ? 9 : version <= 26 ? 11 : 13);
                        for (; count >= 2; count -= 2)
                        {
                            var value = reader.Read(11);
                            if (value >= 45 * 45) return Fail("Invalid character pair.", out error);
                            builder.Append(AlphanumericCharset[value / 45]).Append(AlphanumericCharset[value % 45]);
                        }

                        if (count == 1)
                        {
                            var value = reader.Read(6);
                            if (value >= 45) return Fail("Invalid character.", out error);
                            builder.Append(AlphanumericCharset[value]);
                        }

                        break;
                    }

                case 0b0100:
                    {
                        var count = reader.Read(version <= 9 ? 8 : 16);
                        if (reader.Remaining < count * 8) return Fail("Byte segment is longer than the data.", out error);
                        var bytes = new byte[count];
                        for (var i = 0; i < count; i++)
                        {
                            bytes[i] = (byte)reader.Read(8);
                        }

                        var encoding = eciEncoding ?? (Utf8.IsValid(bytes) ? StrictUtf8 : Encoding.Latin1);
                        try
                        {
                            builder.Append(encoding.GetString(bytes));
                        }
                        catch (DecoderFallbackException)
                        {
                            return Fail("Byte segment does not match the given character encoding.", out error);
                        }

                        break;
                    }

                case 0b0111:
                    {
                        var assignment = reader.Read(8);
                        if ((assignment & 0x80) != 0)
                        {
                            assignment = (assignment & 0x40) == 0
                                ? (assignment & 0x3F) << 8 | reader.Read(8)
                                : (assignment & 0x1F) << 16 | reader.Read(16);
                        }

                        eciEncoding = assignment switch
                        {
                            26 => StrictUtf8,
                            1 or 3 => Encoding.Latin1,
                            27 or 170 => Encoding.ASCII,
                            _ => null,
                        };

                        if (eciEncoding is null) return Fail($"ECI {assignment} is not supported.", out error);
                        break;
                    }

                default:
                    return Fail($"Segment mode {mode:B4} is not supported.", out error);
            }

            if (reader.Overrun) return Fail("Segment extends beyond the end of the data.", out error);
        }

        text = builder.ToString();
        return true;
    }

    private static bool Fail(string message, [NotNullWhen(false)] out string? error)
    {
        error = message;
        return false;
    }

    private ref struct BitReader(ReadOnlySpan<byte> data)
    {
        private readonly ReadOnlySpan<byte> data = data;
        private int position;

        public readonly int Remaining => data.Length * 8 - position;

        public bool Overrun { get; private set; }

        public int Read(int count)
        {
            var value = 0;
            for (var i = 0; i < count; i++, position++)
            {
                var bit = position < data.Length * 8 && (data[position >> 3] >> (7 - (position & 7)) & 1) != 0;
                Overrun |= position >= data.Length * 8;
                value = value << 1 | (bit ? 1 : 0);
            }

            return value;
        }
    }
}
