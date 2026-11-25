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
        QrrQ - QR codes that show a different content when mirrored.

        Usage:
          qrrq "<normal text>" "<mirrored text>" [options]
          qrrq --selftest [count]

        Options:
          -o, --out <name>          File name without extension (default: qrrq)
          --ecc <L|M|Q|H>           Error correction for both directions
          --ecc-normal <L|M|Q|H>    normal direction only
          --ecc-mirrored <L|M|Q|H>  mirrored direction only
          --min-version <1-40>      smallest QR version
          --max-version <1-40>      largest QR version
          --allow-corrections <n>   let up to n codewords per direction be corrected by design (smaller codes)
          --seed <number>           initial value for the free bits
          --attempts <n>            random assignments per mask pair (more = cleaner look, slower)
          --scale <px>              pixels per module (default 10)
          --border <modules>        quiet zone (default 4)
          --svg                     also write SVG files
          --no-preview              no console preview

        Output:
          <name>.png            the print artwork
          <name>.mirrored.png   the mirror image (for testing on screen)
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
            string Next() => i + 1 < args.Length ? args[++i] : throw new ArgumentException($"Missing value for {argument}.");

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
                    throw new ArgumentException($"Unknown option {unknown}.");
                default: texts.Add(argument); break;
            }
        }

        if (texts.Count != 2)
        {
            throw new ArgumentException("Please provide exactly two texts: normal and mirrored.");
        }
    }
    catch (Exception exception) when (exception is ArgumentException or FormatException or OverflowException)
    {
        Console.Error.WriteLine($"Error: {exception.Message}");
        Console.Error.WriteLine("Help: qrrq --help");
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
              normal:   "{normal.Text}"  [ECC {normal.Ecc.Letter}, mask {normal.Mask}, {Health(normal.CorrectedCodewords, code.NormalFormatBitErrors)}]
              mirrored: "{mirrored.Text}"  [ECC {mirrored.Ecc.Letter}, mask {mirrored.Mask}, {Health(mirrored.CorrectedCodewords, code.MirroredFormatBitErrors)}]
              files:    {output}.png, {output}.mirrored.png{(svg ? $", {output}.svg, {output}.mirrored.svg" : "")}
            """);

        if (preview)
        {
            Console.WriteLine();
            Console.WriteLine(code.ToText());
        }

        return 0;
    }

    static char Letter(string value) =>
        value.Length == 1 ? value[0] : throw new FormatException($"'{value}' is not an error correction level (L, M, Q, H).");

    static string Health(int corrected, int formatBits) =>
        (corrected == 0 ? "data error-free" : $"{corrected} codewords pre-corrected") +
        (formatBits == 0 ? ", format exact" : $", format {formatBits} bits corrected");

    static int Fail(QrrQError error)
    {
        Console.Error.WriteLine($"Error: {error.Message}");
        return 1;
    }
}
