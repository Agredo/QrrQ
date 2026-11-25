using System.Buffers;
using System.Text;

namespace QrrQ.Codec;

internal enum SegmentMode
{
    Numeric = 0b0001,
    Alphanumeric = 0b0010,
    Byte = 0b0100,
    Eci = 0b0111,
}

/// <summary>Simple bit sequence modeled on <c>List&lt;bool&gt;</c> (MSB first).</summary>
internal sealed class BitBuffer
{
    private readonly List<bool> bits = [];

    public int Count => bits.Count;

    public bool this[int index] => bits[index];

    public void Append(int value, int length)
    {
        for (var i = length - 1; i >= 0; i--)
        {
            bits.Add((value >> i & 1) != 0);
        }
    }
}

/// <summary>A text encoded as a single segment in the most compact mode available.</summary>
internal sealed class Payload
{
    private const string AlphanumericCharset = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ $%*+-./:";
    private const int Utf8EciAssignment = 26;

    private static readonly SearchValues<char> Alphanumeric = SearchValues.Create(AlphanumericCharset);

    private readonly byte[] bytes;

    private Payload(string text, SegmentMode mode, byte[] bytes, bool usesUtf8Eci)
    {
        Text = text;
        Mode = mode;
        this.bytes = bytes;
        UsesUtf8Eci = usesUtf8Eci;
    }

    public string Text { get; }

    public SegmentMode Mode { get; }

    /// <summary>Non-ASCII texts get an ECI 26 prefix so that scanners reliably assume UTF-8.</summary>
    public bool UsesUtf8Eci { get; }

    private int CharacterCount => Mode == SegmentMode.Byte ? bytes.Length : Text.Length;

    public static Payload From(string text)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        var span = text.AsSpan();
        if (!span.ContainsAnyExceptInRange('0', '9'))
        {
            return new Payload(text, SegmentMode.Numeric, [], false);
        }

        if (!span.ContainsAnyExcept(Alphanumeric))
        {
            return new Payload(text, SegmentMode.Alphanumeric, [], false);
        }

        return new Payload(text, SegmentMode.Byte, Encoding.UTF8.GetBytes(text), !Ascii.IsValid(span));
    }

    /// <summary>Length in bits without terminator, or -1 if the character count exceeds the count field.</summary>
    public int BitLength(int version)
    {
        var countBits = CharacterCountBits(Mode, version);
        if (CharacterCount >= 1 << countBits)
        {
            return -1;
        }

        var dataBits = Mode switch
        {
            SegmentMode.Numeric => CharacterCount / 3 * 10 + (CharacterCount % 3) switch { 2 => 7, 1 => 4, _ => 0 },
            SegmentMode.Alphanumeric => CharacterCount / 2 * 11 + CharacterCount % 2 * 6,
            _ => CharacterCount * 8,
        };

        return (UsesUtf8Eci ? 12 : 0) + 4 + countBits + dataBits;
    }

    /// <summary>Encodes the text as one segment including the terminator. Pad bytes are deliberately left free.</summary>
    public BitBuffer Encode(int version, int capacityBits) => Encode(version, capacityBits, split: -1)!;

    /// <summary>
    /// Equivalent encodings of the same text: one segment or two segments with a split position.
    /// Every scanner reassembles the segments into the same text, but the bit patterns differ -
    /// which multiplies the chances that both messages match in the shared corner.
    /// </summary>
    public IEnumerable<BitBuffer> EncodeVariants(int version, int capacityBits, int maxVariants)
    {
        yield return Encode(version, capacityBits);

        // Texts with an ECI identifier (UTF-8) stay a single segment: after an ECI switch OpenCV
        // decodes only the last segment correctly (measured with OpenCV 5.0).
        if (UsesUtf8Eci)
        {
            yield break;
        }

        // Split positions: never inside a UTF-8 character, distributed evenly over the text.
        int[] splits = Mode == SegmentMode.Byte
            ? [.. Enumerable.Range(1, Math.Max(0, bytes.Length - 1)).Where(p => (bytes[p] & 0xC0) != 0x80)]
            : [.. Enumerable.Range(1, Math.Max(0, Text.Length - 1))];
        var take = Math.Min(splits.Length, maxVariants - 1);
        for (var i = 0; i < take; i++)
        {
            var split = splits[(int)((long)i * splits.Length / take)];
            if (Encode(version, capacityBits, split) is { } variant)
            {
                yield return variant;
            }
        }
    }

    private BitBuffer? Encode(int version, int capacityBits, int split)
    {
        var buffer = new BitBuffer();
        if (UsesUtf8Eci)
        {
            buffer.Append((int)SegmentMode.Eci, 4);
            buffer.Append(Utf8EciAssignment, 8);
        }

        if (split < 0)
        {
            AppendSegment(buffer, version, 0, CharacterCount);
        }
        else
        {
            AppendSegment(buffer, version, 0, split);
            AppendSegment(buffer, version, split, CharacterCount - split);
        }

        if (buffer.Count > capacityBits)
        {
            return null;
        }

        // Terminator and zero bits up to the byte boundary are fixed: for ECI codes OpenCV reads past
        // the terminator up to the byte boundary. Only the pad bytes after that are freely choosable.
        buffer.Append(0, Math.Min(4, capacityBits - buffer.Count));
        buffer.Append(0, Math.Min((8 - buffer.Count % 8) % 8, capacityBits - buffer.Count));
        return buffer;
    }

    private void AppendSegment(BitBuffer buffer, int version, int start, int count)
    {
        buffer.Append((int)Mode, 4);
        buffer.Append(count, CharacterCountBits(Mode, version));

        switch (Mode)
        {
            case SegmentMode.Numeric:
                for (var i = start; i < start + count; i += 3)
                {
                    var digits = Math.Min(3, start + count - i);
                    buffer.Append(int.Parse(Text.AsSpan(i, digits)), digits * 3 + 1);
                }

                break;

            case SegmentMode.Alphanumeric:
                for (var i = start; i < start + count; i += 2)
                {
                    var value = AlphanumericCharset.IndexOf(Text[i]);
                    if (i + 1 < start + count)
                    {
                        buffer.Append(value * 45 + AlphanumericCharset.IndexOf(Text[i + 1]), 11);
                    }
                    else
                    {
                        buffer.Append(value, 6);
                    }
                }

                break;

            default:
                foreach (var b in bytes.AsSpan(start, count))
                {
                    buffer.Append(b, 8);
                }

                break;
        }
    }

    private static int CharacterCountBits(SegmentMode mode, int version) => (mode, version) switch
    {
        (SegmentMode.Numeric, <= 9) => 10,
        (SegmentMode.Numeric, <= 26) => 12,
        (SegmentMode.Numeric, _) => 14,
        (SegmentMode.Alphanumeric, <= 9) => 9,
        (SegmentMode.Alphanumeric, <= 26) => 11,
        (SegmentMode.Alphanumeric, _) => 13,
        (SegmentMode.Byte, <= 9) => 8,
        (SegmentMode.Byte, _) => 16,
        _ => throw new ArgumentOutOfRangeException(nameof(mode)),
    };
}
