using System.Buffers.Binary;
using System.Text;

namespace QrrQ.Tests;

public class RenderingTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    [Fact]
    public void Png_output_has_signature_and_expected_dimensions()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var png = code.ToPng(moduleSize: 4, quietZone: 3);

        Assert.Equal(PngSignature, png[..8]);
        var expected = (code.Size + 2 * 3) * 4;
        Assert.Equal(expected, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(16, 4)));
        Assert.Equal(expected, BinaryPrimitives.ReadInt32BigEndian(png.AsSpan(20, 4)));
        Assert.Contains("IEND"u8.ToArray(), png);
    }

    [Fact]
    public void Mirrored_png_differs_from_normal_png()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");

        var (normal, mirrored) = code.ToPngPair();

        Assert.NotEqual(normal, mirrored);
        Assert.Equal(code.ToPng(), normal);
        Assert.Equal(code.ToPng(mirrored: true), mirrored);
    }

    [Fact]
    public void Png_rejects_invalid_parameters()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");

        Assert.Throws<ArgumentOutOfRangeException>(() => code.ToPng(moduleSize: 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => code.ToPng(quietZone: -1));
    }

    [Fact]
    public void Svg_contains_a_single_path_and_correct_viewbox()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var svg = code.ToSvg(moduleSize: 8, quietZone: 4);

        Assert.StartsWith("<svg", svg, StringComparison.Ordinal);
        Assert.Contains($"viewBox=\"0 0 {code.Size + 8} {code.Size + 8}\"", svg, StringComparison.Ordinal);
        Assert.Contains($"width=\"{(code.Size + 8) * 8}\"", svg, StringComparison.Ordinal);
        Assert.Single(Enumerate("<path", svg));
        Assert.Single(Enumerate("<path", code.ToSvg(mirrored: true)));
    }

    [Fact]
    public void Svg_can_use_custom_colors_and_rejects_invalid_sizes()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");

        var svg = code.ToSvg(darkColor: "#123456", lightColor: "#abcdef");
        Assert.Contains("#123456", svg, StringComparison.Ordinal);
        Assert.Contains("#abcdef", svg, StringComparison.Ordinal);
        Assert.Throws<ArgumentOutOfRangeException>(() => code.ToSvg(moduleSize: 0));
    }

    [Fact]
    public void Text_preview_uses_half_block_characters()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var preview = code.ToText();

        Assert.NotEmpty(preview);
        Assert.Contains(preview, static c => c is '█' or '▀' or '▄');
        Assert.NotEqual(preview, code.ToText(mirrored: true));
        Assert.NotEqual(preview, code.ToText(darkBackground: false));
    }

    [Fact]
    public void Packed_rows_match_matrix()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var packed = code.Matrix.ToPackedRows();
        var stride = (code.Size + 7) / 8;

        Assert.Equal(stride * code.Size, packed.Length);
        for (var row = 0; row < code.Size; row++)
        {
            for (var column = 0; column < code.Size; column++)
            {
                var bit = (packed[row * stride + (column >> 3)] & (0x80 >> (column & 7))) != 0;
                Assert.Equal(code.Matrix[row, column], bit);
            }
        }
    }

    private static IEnumerable<string> Enumerate(string needle, string text)
    {
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            yield return needle;
            index += needle.Length;
        }
    }
}
