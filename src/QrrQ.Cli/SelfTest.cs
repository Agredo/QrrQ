using System.Diagnostics;
using QrrQ.Reading;

namespace QrrQ.Cli;

/// <summary>Zufällige Rundreisen: erzeugen → normal und gespiegelt lesen → vergleichen.</summary>
internal static class SelfTest
{
    private static readonly string[] Samples =
    [
        "https://example.com/raetsel",
        "HTTPS://QRRQ.DE/A",
        "Falsche Fährte",
        "Schau in den Spiegel",
        "Code: 4711",
        "0815",
        "Grüße aus Bremen 🌊",
        "WIFI:T:WPA;S:Escape;P:spiegel123;;",
        "Raum 3 – Tresor links",
        "geheim",
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
                Console.WriteLine($"  ✗ keine Lösung: \"{normal}\" / \"{mirrored}\"");
                continue;
            }

            var (readNormal, readMirrored) = code.Verify();
            var rotated = QrReader.Read(code.MirroredMatrix.RotateClockwise()); // Scanner hält das Handy schief
            if (readNormal.Text != normal || readMirrored.Text != mirrored || rotated.Text != mirrored)
            {
                failures++;
                Console.WriteLine($"  ✗ falsch gelesen: \"{readNormal.Text}\" / \"{readMirrored.Text}\"");
                continue;
            }

            if (code.IsErrorFree && !SurvivesDamage(code, random))
            {
                failures++;
                Console.WriteLine($"  ✗ Fehlerkorrektur versagt bei V{code.Version}");
                continue;
            }

            ok++;
            errorFree += code.IsErrorFree ? 1 : 0;
            versions[code.Version] = versions.GetValueOrDefault(code.Version) + 1;
        }

        Console.WriteLine($"""

            Selbsttest: {ok}/{count} korrekt, davon {errorFree} komplett fehlerfrei, {failures} Fehlschläge
            Dauer: {stopwatch.Elapsed.TotalSeconds:F1} s
            Versionen: {string.Join(", ", versions.Select(v => $"V{v.Key}×{v.Value}"))}
            """);
        return failures == 0 ? 0 : 1;
    }

    private static string Pick(Random random) =>
        random.Next(4) == 0 ? random.NextInt64(1, 99_999_999).ToString() : Samples[random.Next(Samples.Length)];

    /// <summary>Kippt ein paar Datenmodule und prüft, dass beide Richtungen weiterhin lesbar sind.</summary>
    private static bool SurvivesDamage(QrrQCode code, Random random)
    {
        var modules = code.Matrix.ToArray();
        var size = code.Size;
        for (var flips = 0; flips < 2; flips++)
        {
            // Rechte untere Ecke meiden (dort liegen die Nachrichtenanfänge beider Richtungen dicht beieinander).
            int row = random.Next(9, size - 12), column = random.Next(9, size - 12);
            modules[row, column] = !modules[row, column];
        }

        var damaged = new QrMatrix(modules);
        return QrReader.TryRead(damaged, out var normal, out _) && normal.Text == code.NormalText &&
               QrReader.TryRead(damaged.FlipHorizontal(), out var mirrored, out _) && mirrored.Text == code.MirroredText;
    }
}
