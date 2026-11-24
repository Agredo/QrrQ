using System.Diagnostics;
using QrrQ;
using QrrQ.Cli;

return args switch
{
    [] or ["-h" or "--help" or "/?"] => Usage(),
    ["--selftest"] => SelfTest.Run(60),
    ["--selftest", var count] when int.TryParse(count, out var n) => SelfTest.Run(n),
    _ => Generate(args),
};

static int Usage()
{
    Console.WriteLine("""
        QrrQ – QR-Codes, die gespiegelt einen anderen Inhalt zeigen.

        Aufruf:
          qrrq "<Text normal>" "<Text gespiegelt>" [Optionen]
          qrrq --selftest [Anzahl]

        Optionen:
          -o, --out <name>          Dateiname ohne Endung (Standard: qrrq)
          --ecc <L|M|Q|H>           Fehlerkorrektur für beide Richtungen
          --ecc-normal <L|M|Q|H>    nur normale Richtung
          --ecc-mirrored <L|M|Q|H>  nur gespiegelte Richtung
          --min-version <1-40>      kleinste QR-Version
          --max-version <1-40>      größte QR-Version
          --allow-corrections <n>   bis zu n Codewörter pro Richtung ab Werk korrigieren lassen (kleinere Codes)
          --seed <zahl>             Startwert für die freien Bits
          --attempts <n>            Zufallsbelegungen pro Maskenpaar (mehr = sauberere Optik, langsamer)
          --scale <px>              Pixel pro Modul (Standard 10)
          --border <module>         Ruhezone (Standard 4)
          --svg                     zusätzlich SVG-Dateien schreiben
          --no-preview              keine Konsolenvorschau

        Ausgabe:
          <name>.png            das Druckmotiv
          <name>.mirrored.png   das Spiegelbild (zum Testen am Bildschirm)
        """);
    return 0;
}

static int Generate(string[] args)
{
    List<string> texts = [];
    var output = "qrrq";
    var moduleSize = 10;
    var quietZone = 4;
    var svg = false;
    var preview = true;
    var options = QrrQOptions.Default;

    try
    {
        for (var i = 0; i < args.Length; i++)
        {
            var argument = args[i];
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Für {argument} fehlt ein Wert.");

            switch (argument)
            {
                case "-o" or "--out": output = Next(); break;
                case "--ecc":
                    var both = EccLevel.FromLetter(Letter(Next()));
                    options = options with { NormalEcc = both, MirroredEcc = both };
                    break;
                case "--ecc-normal": options = options with { NormalEcc = EccLevel.FromLetter(Letter(Next())) }; break;
                case "--ecc-mirrored": options = options with { MirroredEcc = EccLevel.FromLetter(Letter(Next())) }; break;
                case "--min-version": options = options with { MinVersion = int.Parse(Next()) }; break;
                case "--max-version": options = options with { MaxVersion = int.Parse(Next()) }; break;
                case "--allow-corrections": options = options with { MaxCorrectedCodewords = int.Parse(Next()) }; break;
                case "--seed": options = options with { Seed = int.Parse(Next()) }; break;
                case "--attempts": options = options with { AttemptsPerCandidate = int.Parse(Next()) }; break;
                case "--scale": moduleSize = int.Parse(Next()); break;
                case "--border": quietZone = int.Parse(Next()); break;
                case "--svg": svg = true; break;
                case "--no-preview": preview = false; break;
                case var unknown when unknown.StartsWith("--", StringComparison.Ordinal):
                    throw new ArgumentException($"Unbekannte Option {unknown}.");
                default: texts.Add(argument); break;
            }
        }

        if (texts.Count != 2)
        {
            throw new ArgumentException("Bitte genau zwei Texte angeben: normal und gespiegelt.");
        }
    }
    catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
    {
        Console.Error.WriteLine($"Fehler: {exception.Message}");
        Console.Error.WriteLine("Hilfe: qrrq --help");
        return 2;
    }

    var stopwatch = Stopwatch.StartNew();
    var result = QrrQGenerator.TryCreate(texts[0], texts[1], options);
    stopwatch.Stop();

    return result switch
    {
        QrrQCode code => Save(code),
        QrrQError error => Fail(error),
    };

    int Save(QrrQCode code)
    {
        var (png, mirroredPng) = code.ToPngPair(moduleSize, quietZone);
        File.WriteAllBytes($"{output}.png", png);
        File.WriteAllBytes($"{output}.mirrored.png", mirroredPng);
        if (svg)
        {
            File.WriteAllText($"{output}.svg", code.ToSvg(moduleSize, quietZone));
            File.WriteAllText($"{output}.mirrored.svg", code.ToSvg(moduleSize, quietZone, mirrored: true));
        }

        var (normal, mirrored) = code.Verify();
        Console.WriteLine($"""
            QrrQ Version {code.Version} ({code.Size}×{code.Size} Module) in {stopwatch.ElapsedMilliseconds} ms
              normal:     "{normal.Text}"  [ECC {normal.Ecc.Letter}, Maske {normal.Mask}, {Health(normal.CorrectedCodewords, code.NormalFormatBitErrors)}]
              gespiegelt: "{mirrored.Text}"  [ECC {mirrored.Ecc.Letter}, Maske {mirrored.Mask}, {Health(mirrored.CorrectedCodewords, code.MirroredFormatBitErrors)}]
              Dateien:    {output}.png, {output}.mirrored.png{(svg ? $", {output}.svg, {output}.mirrored.svg" : "")}
            """);

        if (preview)
        {
            Console.WriteLine();
            Console.WriteLine(code.ToText());
        }

        return 0;
    }

    static char Letter(string value) =>
        value.Length == 1 ? value[0] : throw new FormatException($"'{value}' ist keine Fehlerkorrekturstufe (L, M, Q, H).");

    static string Health(int corrected, int formatBits) =>
        (corrected == 0 ? "Daten fehlerfrei" : $"{corrected} Codewörter vorkorrigiert") +
        (formatBits == 0 ? ", Format exakt" : $", Format {formatBits} Bit korrigiert");

    static int Fail(QrrQError error)
    {
        Console.Error.WriteLine($"Fehler: {error.Message}");
        return 1;
    }
}
