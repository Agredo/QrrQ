using System.Diagnostics;
using QrrQ.Reading;

namespace QrrQ.Cli;

/// <summary>Random round trips: generate -> read normal and mirrored -> compare.</summary>
internal static class SelfTest
{
    private static readonly string[] Samples =
    [
        "https://example.com/puzzle",
        "HTTPS://QRRQ.DE/A",
        "False trail",
        "Look in the mirror",
        "Code: 4711",
        "0815",
        "Greetings from Bremen 🌊",
        "WIFI:T:WPA;S:Escape;P:mirror123;;",
        "Room 3 - safe on the left",
        "secret",
    ];

    public static int Run(int count)
    {
        var random = new Random(42);
        var stopwatch = Stopwatch.StartNew();
        int ok = 0, errorFree = 0, failures = 0;
        var versions = new SortedDictionary<int, int>();

        for (var i = 0; i < count; i++)
        {
            var normal = Pick(random);
            var mirrored = Pick(random);
            while (mirrored == normal)
            {
                mirrored = Pick(random);
            }

            var options = new QrrQOptions { Seed = random.Next(), MaxCorrectedCodewords = i % 3 == 2 ? 4 : 0 };
            var result = QrrQGenerator.TryCreate(normal, mirrored, options);
            if (result.Value is not QrrQCode code)
            {
                failures++;
                Console.WriteLine($"  ✗ no solution: \"{normal}\" / \"{mirrored}\"");
                continue;
            }

            var (readNormal, readMirrored) = code.Verify();
            var rotated = QrReader.Read(code.MirroredMatrix.RotateClockwise()); // the scanner holds the phone at an angle
            if (readNormal.Text != normal || readMirrored.Text != mirrored || rotated.Text != mirrored)
            {
                failures++;
                Console.WriteLine($"  ✗ read incorrectly: \"{readNormal.Text}\" / \"{readMirrored.Text}\"");
                continue;
            }

            if (code.IsErrorFree && !SurvivesDamage(code, random))
            {
                failures++;
                Console.WriteLine($"  ✗ error correction failed at V{code.Version}");
                continue;
            }

            ok++;
            errorFree += code.IsErrorFree ? 1 : 0;
            versions[code.Version] = versions.GetValueOrDefault(code.Version) + 1;
        }

        Console.WriteLine($"""

            Self-test: {ok}/{count} correct, of which {errorFree} completely error-free, {failures} failures
            Duration: {stopwatch.Elapsed.TotalSeconds:F1} s
            Versions: {string.Join(", ", versions.Select(v => $"V{v.Key}×{v.Value}"))}
            """);
        return failures == 0 ? 0 : 1;
    }

    private static string Pick(Random random) =>
        random.Next(4) == 0 ? random.NextInt64(1, 99_999_999).ToString() : Samples[random.Next(Samples.Length)];

    /// <summary>Flips a few data modules and checks that both directions remain readable.</summary>
    private static bool SurvivesDamage(QrrQCode code, Random random)
    {
        var modules = code.Matrix.ToArray();
        var size = code.Size;
        for (var flips = 0; flips < 2; flips++)
        {
            // Avoid the bottom-right corner (that is where the message starts of both directions sit close together).
            int row = random.Next(9, size - 12), column = random.Next(9, size - 12);
            modules[row, column] = !modules[row, column];
        }

        var damaged = new QrMatrix(modules);
        return QrReader.TryRead(damaged, out var normal, out _) && normal.Text == code.NormalText &&
               QrReader.TryRead(damaged.FlipHorizontal(), out var mirrored, out _) && mirrored.Text == code.MirroredText;
    }
}
