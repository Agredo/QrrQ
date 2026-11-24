using QrrQ.Reading;

namespace QrrQ.Tests;

public class ApiTests
{
    [Fact]
    public void TryCreate_returns_union_with_code()
    {
        var result = QrrQGenerator.TryCreate("Falsche Fährte", "Code: 4711");

        var code = Assert.IsType<QrrQCode>(result.Value);
        Assert.Equal("Falsche Fährte", code.NormalText);
        Assert.Equal("Code: 4711", code.MirroredText);
    }

    [Fact]
    public void Create_throws_with_structured_error_when_no_solution_exists()
    {
        var exception = Assert.Throws<QrrQException>(() =>
            QrrQGenerator.Create(
                "https://example.com/abc1234",
                "https://qrrq.de/xyz9876",
                new QrrQOptions { MaxVersion = 7 }));

        Assert.IsType<NoSolutionFound>(exception.Error);
        Assert.Contains("Version", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Oversized_payload_reports_PayloadTooLong()
    {
        var normal = new string('A', 5_000);
        var mirrored = new string('B', 5_000);

        var result = QrrQGenerator.TryCreate(normal, mirrored);

        var error = Assert.IsType<PayloadTooLong>(result.Value);
        Assert.True(error.NormalBits > 0);
        Assert.Equal(40, error.MaxVersion);
        Assert.Contains("zu lang", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void High_against_high_has_no_room_for_two_payloads()
    {
        var options = new QrrQOptions { NormalEcc = EccLevel.High, MirroredEcc = EccLevel.High };

        var result = QrrQGenerator.TryCreate("A", "B", options);

        var error = Assert.IsType<IncompatibleEccLevels>(result.Value);
        Assert.Equal(EccLevel.High, error.Normal);
        Assert.Equal(EccLevel.High, error.Mirrored);
    }

    [Fact]
    public void Explicit_ecc_levels_are_used()
    {
        var options = new QrrQOptions { NormalEcc = EccLevel.Low, MirroredEcc = EccLevel.Low };
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711", options);

        Assert.Equal(EccLevel.Low, code.NormalEcc);
        Assert.Equal(EccLevel.Low, code.MirroredEcc);
        Assert.Equal('L', code.NormalEcc.Letter);
    }

    [Fact]
    public void Version_bounds_are_respected()
    {
        var options = new QrrQOptions { MinVersion = 5, MaxVersion = 6 };
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711", options);

        Assert.InRange(code.Version, 5, 6);
        Assert.Equal((code.Version * 4) + 17, code.Size);
    }

    [Fact]
    public void Corrections_allow_smaller_codes_but_are_reported()
    {
        const string normal = "https://example.com/abc1234";
        const string mirrored = "https://qrrq.de/xyz9876";
        var without = QrrQGenerator.TryCreate(normal, mirrored, new QrrQOptions { MaxVersion = 7 });
        var with = QrrQGenerator.Create(normal, mirrored, new QrrQOptions { MaxVersion = 7, MaxCorrectedCodewords = 4 });

        Assert.IsType<NoSolutionFound>(without.Value);
        Assert.False(with.IsErrorFree);
        Assert.InRange(with.Version, 1, 7);
        Assert.Equal(normal, with.Verify().Normal.Text);
        Assert.Equal(mirrored, with.Verify().Mirrored.Text);
    }

    [Fact]
    public void Empty_inputs_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => QrrQGenerator.Create("", "B"));
        Assert.Throws<ArgumentException>(() => QrrQGenerator.Create("A", ""));
        Assert.Throws<ArgumentNullException>(() => QrrQGenerator.Create(null!, "B"));
    }

    [Fact]
    public void Inverted_version_range_is_rejected()
    {
        var options = new QrrQOptions { MinVersion = 10, MaxVersion = 2 };

        Assert.Throws<ArgumentException>(() => QrrQGenerator.TryCreate("A", "B", options));
    }

    [Fact]
    public void Invalid_option_values_are_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new QrrQOptions { MinVersion = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QrrQOptions { MaxVersion = 41 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QrrQOptions { MaxCorrectedCodewords = -1 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new QrrQOptions { AttemptsPerCandidate = 0 });
        Assert.Throws<FormatException>(() => EccLevel.FromLetter('X'));
    }

    [Fact]
    public void Verify_reports_consistent_metadata()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
        var (normal, mirrored) = code.Verify();

        Assert.Equal(code.Version, normal.Version);
        Assert.Equal(code.NormalEcc, normal.Ecc);
        Assert.Equal(code.NormalMask, normal.Mask);
        Assert.Equal(code.NormalCorrectedCodewords, normal.CorrectedCodewords);
        Assert.Equal(code.MirroredCorrectedCodewords, mirrored.CorrectedCodewords);
        Assert.Contains("QrrQ V", code.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void Code_properties_describe_the_payload()
    {
        var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");

        Assert.Equal("Falsche Fährte", code.NormalText);
        Assert.Equal("Code: 4711", code.MirroredText);
        Assert.InRange(code.Version, 1, 40);
        Assert.Equal(code.Matrix.Size, code.Size);
        Assert.Equal(code.Matrix.FlipHorizontal(), code.MirroredMatrix);
        Assert.Equal(QrReader.Read(code.Matrix).Text, code.Verify().Normal.Text);
    }
}
