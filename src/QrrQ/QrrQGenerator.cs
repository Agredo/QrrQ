using QrrQ.Codec;

namespace QrrQ;

/// <summary>
/// Erzeugt QR-Codes, die normal gescannt einen Text liefern und gespiegelt einen anderen.
/// </summary>
/// <example>
/// <code>
/// var code = QrrQGenerator.Create("Falsche Fährte", "Code: 4711");
/// File.WriteAllBytes("raetsel.png", code.ToPng());
/// </code>
/// </example>
public static class QrrQGenerator
{
    /// <summary>Erzeugt den Code oder wirft eine <see cref="QrrQException"/>.</summary>
    /// <param name="normal">Inhalt beim normalen Scannen.</param>
    /// <param name="mirrored">Inhalt beim Scannen des Spiegelbilds.</param>
    /// <param name="options">Optionale Einstellungen.</param>
    public static QrrQCode Create(string normal, string mirrored, QrrQOptions? options = null)
    {
        var result = TryCreate(normal, mirrored, options);
        return result switch
        {
            QrrQCode code => code,
            QrrQError error => throw new QrrQException(error),
        };
    }

    /// <summary>Erzeugt den Code; Fehlschläge kommen als <see cref="QrrQError"/> im Union-Ergebnis zurück.</summary>
    /// <param name="normal">Inhalt beim normalen Scannen.</param>
    /// <param name="mirrored">Inhalt beim Scannen des Spiegelbilds.</param>
    /// <param name="options">Optionale Einstellungen.</param>
    public static QrrQResult TryCreate(string normal, string mirrored, QrrQOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(normal);
        ArgumentException.ThrowIfNullOrEmpty(mirrored);
        options ??= QrrQOptions.Default;
        if (options.MinVersion > options.MaxVersion)
        {
            throw new ArgumentException("MinVersion darf nicht größer als MaxVersion sein.", nameof(options));
        }

        var payloadA = Payload.From(normal);
        var payloadB = Payload.From(mirrored);
        var pairs = options.CandidateEccPairs();
        var settings = new SolverSettings(options.MaxCorrectedCodewords, options.AttemptsPerCandidate, new Random(options.Seed));

        MirrorSolution? solution = null;
        var anyFits = false;

        search: for (var version = options.MinVersion; version <= options.MaxVersion; version++)
        {
            List<(EccLevel Normal, EccLevel Mirrored)> fitting = [with(capacity: pairs.Count)];
            fitting.AddRange(pairs.Where(p => MirrorSolver.Fits(version, p.Normal, p.Mirrored, payloadA, payloadB)));
            if (fitting.Count == 0)
            {
                continue;
            }

            anyFits = true;
            foreach (var (eccA, eccB) in fitting)
            {
                if (MirrorSolver.Solve(version, eccA, eccB, payloadA, payloadB, settings, withCorrections: false) is { } errorFree)
                {
                    solution = errorFree;
                    break search;
                }
            }

            if (options.MaxCorrectedCodewords == 0)
            {
                continue;
            }

            foreach (var (eccA, eccB) in fitting)
            {
                if (MirrorSolver.Solve(version, eccA, eccB, payloadA, payloadB, settings, withCorrections: true) is { } corrected)
                {
                    solution = corrected;
                    break search;
                }
            }
        }

        if (solution is not null)
        {
            return new QrrQCode(solution);
        }

        if (anyFits)
        {
            return Fail(new NoSolutionFound(options.MinVersion, options.MaxVersion, options.MaxCorrectedCodewords));
        }

        var headroom = pairs.Any(p => Enumerable.Range(options.MinVersion, options.MaxVersion - options.MinVersion + 1)
            .Any(v => MirrorSolver.HasHeadroom(v, p.Normal, p.Mirrored)));
        return headroom
            ? Fail(new PayloadTooLong(payloadA.BitLength(options.MaxVersion), payloadB.BitLength(options.MaxVersion), options.MaxVersion))
            : Fail(new IncompatibleEccLevels(pairs[0].Normal, pairs[0].Mirrored));
    }

    private static QrrQResult Fail(QrrQError error) => error;
}
