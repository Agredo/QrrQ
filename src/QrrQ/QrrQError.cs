namespace QrrQ;

/// <summary>
/// Reason why no mirror QR code could be produced.
/// Closed hierarchy (C# 15): all cases live in this file, so a <c>switch</c>
/// over it is exhaustive without a default branch.
/// </summary>
public closed record class QrrQError;

/// <summary>At least one text does not fit in any allowed version.</summary>
/// <param name="NormalBits">Bits required for the normal text (in the largest version).</param>
/// <param name="MirroredBits">Bits required for the mirrored text.</param>
/// <param name="MaxVersion">Largest version that was checked.</param>
public sealed record class PayloadTooLong(int NormalBits, int MirroredBits, int MaxVersion) : QrrQError;

/// <summary>The chosen error correction levels leave no room for two payloads.</summary>
/// <param name="Normal">Level used for normal reading.</param>
/// <param name="Mirrored">Level used for mirrored reading.</param>
public sealed record class IncompatibleEccLevels(EccLevel Normal, EccLevel Mirrored) : QrrQError;

/// <summary>The texts fit, but no solution was found inside the version range.</summary>
/// <param name="MinVersion">Smallest version that was checked.</param>
/// <param name="MaxVersion">Largest version that was checked.</param>
/// <param name="MaxCorrectedCodewords">Allowed pre-corrected codewords.</param>
public sealed record class NoSolutionFound(int MinVersion, int MaxVersion, int MaxCorrectedCodewords) : QrrQError;

/// <summary>Extension members for <see cref="QrrQError"/>.</summary>
public static class QrrQErrorExtensions
{
    extension(QrrQError error)
    {
        /// <summary>Human readable error description.</summary>
        public string Message => error switch
        {
            PayloadTooLong e =>
                $"The texts are too long: normal {e.NormalBits} bits, mirrored {e.MirroredBits} bits - " +
                $"even version {e.MaxVersion} is not enough. Shorten the texts or pick a lower error correction level.",
            IncompatibleEccLevels e =>
                $"With {e.Normal.Letter}/{e.Mirrored.Letter} there is no room for two payloads: the data capacities " +
                "of both directions combined must be larger than all codewords. Combine H only with L, Q with L or M.",
            NoSolutionFound e =>
                $"No solution in versions {e.MinVersion}-{e.MaxVersion} " +
                (e.MaxCorrectedCodewords == 0
                    ? "without error correction budget. Tip: allow MaxCorrectedCodewords > 0 or widen the version range."
                    : $"with at most {e.MaxCorrectedCodewords} pre-corrected codewords."),
        };
    }
}

/// <summary>Thrown by <see cref="QrrQGenerator.Create"/>.</summary>
public sealed class QrrQException(QrrQError error) : Exception(error.Message)
{
    /// <summary>The structured error reason.</summary>
    public QrrQError Error { get; } = error;
}