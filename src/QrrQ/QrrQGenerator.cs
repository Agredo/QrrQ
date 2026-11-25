using QrrQ.Codec;

namespace QrrQ;

/// <summary>
/// Creates QR codes that yield one text when scanned normally and another one when mirrored.
/// </summary>
/// <example>
/// <code>
/// var code = QrrQGenerator.Create("Decoy", "Code: 4711");
/// File.WriteAllBytes("puzzle.png", code.ToPng());
/// </code>
/// </example>
public static class QrrQGenerator
{
    /// <summary>Creates the code or throws a <see cref="QrrQException"/>.</summary>
    /// <param name="normal">Payload for normal scanning.</param>
    /// <param name="mirrored">Payload for scanning the mirror image.</param>
    /// <param name="options">Optional settings.</param>
    public static QrrQCode Create(string normal, string mirrored, QrrQOptions? options = null)
    {
        var result = TryCreate(normal, mirrored, options);
        return result switch
        {
            QrrQCode code => code,
            QrrQError error => throw new QrrQException(error),
        };
    }

    /// <summary>Creates the code; failures come back as a <see cref="QrrQError"/> in the union result.</summary>
    /// <param name="normal">Payload for normal scanning.</param>
    /// <param name="mirrored">Payload for scanning the mirror image.</param>
    /// <param name="options">Optional settings.</param>
    public static QrrQResult TryCreate(string normal, string mirrored, QrrQOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(normal);
        ArgumentException.ThrowIfNullOrEmpty(mirrored);
        options ??= QrrQOptions.Default;
        if (options.MinVersion > options.MaxVersion)
        {
            throw new ArgumentException("MinVersion must not be greater than MaxVersion.", nameof(options));
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
