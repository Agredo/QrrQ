using QrrQ.Reading;

namespace QrrQ.Tests;

public class RoundTripTests
{
    [Theory]
    [InlineData("Falsche Fährte", "Code: 4711")]
    [InlineData("https://example.com/raetsel", "geheim")]
    [InlineData("0815", "Schau in den Spiegel")]
    [InlineData("Raum 3 – Tresor links", "https://qrrq.de/x")]
    public void Create_reads_both_directions(string normal, string mirrored)
    {
        var code = QrrQGenerator.Create(normal, mirrored);
        var (readNormal, readMirrored) = code.Verify();

        Assert.Equal(normal, readNormal.Text);
        Assert.Equal(mirrored, readMirrored.Text);
        Assert.Equal(code.Version, readNormal.Version);
        Assert.Equal(code.Version, readMirrored.Version);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Scanner_handles_all_right_angle_rotations(int quarterTurns)
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var matrix = code.Matrix;
        for (var turn = 0; turn < quarterTurns; turn++)
        {
            matrix = matrix.RotateClockwise();
        }

        Assert.Equal(code.NormalText, QrReader.Read(matrix).Text);
    }

    [Fact]
    public void Mirrored_view_reads_the_second_text()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");

        Assert.Equal(code.NormalText, QrReader.Read(code.Matrix).Text);
        Assert.Equal(code.MirroredText, QrReader.Read(code.MirroredMatrix).Text);
    }

    [Fact]
    public void Umptext_and_emoji_are_encoded_as_utf8()
    {
        var code = QrrQGenerator.Create("Grüße aus Bremen 🌊", "WLAN: Spiegel123");

        Assert.Equal("Grüße aus Bremen 🌊", code.Verify().Normal.Text);
        Assert.Equal("WLAN: Spiegel123", code.Verify().Mirrored.Text);
    }

    [Fact]
    public void Same_seed_produces_identical_matrix()
    {
        var options = new QrrQOptions { Seed = 12345 };
        var first = QrrQGenerator.Create("A", "B", options);
        var second = QrrQGenerator.Create("A", "B", options);
        var other = QrrQGenerator.Create("A", "B", options with { Seed = 54321 });

        Assert.Equal(first.Matrix, second.Matrix);
        Assert.NotEqual(first.Matrix, other.Matrix);
    }

    [Fact]
    public void Error_free_code_survives_a_couple_of_flipped_modules()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        Assert.True(code.IsErrorFree);

        var modules = code.Matrix.ToArray();
        modules[code.Size / 2, code.Size / 2 + 3] = !modules[code.Size / 2, code.Size / 2 + 3];
        modules[code.Size / 2 + 4, code.Size / 2 + 1] = !modules[code.Size / 2 + 4, code.Size / 2 + 1];
        var damaged = new QrMatrix(modules);

        Assert.Equal(code.NormalText, QrReader.Read(damaged).Text);
        Assert.Equal(code.MirroredText, QrReader.Read(damaged.FlipHorizontal()).Text);
    }

    [Fact]
    public void Format_information_stays_within_correction_range()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");

        Assert.InRange(code.NormalFormatBitErrors, 0, 3);
        Assert.InRange(code.MirroredFormatBitErrors, 0, 3);
    }
}
