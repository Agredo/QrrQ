namespace QrrQ;

/// <summary>Settings for <see cref="QrrQGenerator"/>.</summary>
public sealed record QrrQOptions
{
    /// <summary>Default settings: automatic error correction (M/L), version 1-40, strictly error-free.</summary>
    public static QrrQOptions Default { get; } = new();

    /// <summary>
    /// Error correction for normal reading. <c>null</c> = automatic (M first, then L).
    /// Note: the data capacities of both directions combined must be larger than the
    /// total number of codewords - so H only works with L, Q with L (or barely with M).
    /// </summary>
    public EccLevel? NormalEcc { get; init; }

    /// <summary>Error correction for mirrored reading. <c>null</c> = automatic.</summary>
    public EccLevel? MirroredEcc { get; init; }

    /// <summary>Smallest allowed QR version (1-40).</summary>
    public int MinVersion
    {
        get;
        init => field = ValidateVersion(value);
    } = 1;

    /// <summary>Largest allowed QR version (1-40).</summary>
    public int MaxVersion
    {
        get;
        init => field = ValidateVersion(value);
    } = 40;

    /// <summary>
    /// How many codewords per reading direction may already have been repaired by
    /// error correction in the printed code. <c>0</c> (default) means: both directions
    /// are error-free Reed-Solomon codewords. Values &gt; 0 allow smaller codes for
    /// longer texts but cost robustness.
    /// </summary>
    public int MaxCorrectedCodewords
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxCorrectedCodewords));
    }

    /// <summary>Seed for the freely chosen bits - same seed, same input, same code.</summary>
    public int Seed { get; init; } = 0x5152;

    /// <summary>Random assignments per mask combination; the visually best candidate wins.</summary>
    public int AttemptsPerCandidate
    {
        get;
        init => field = value >= 1 ? value : throw new ArgumentOutOfRangeException(nameof(AttemptsPerCandidate));
    } = 2;

    internal IReadOnlyList<(EccLevel Normal, EccLevel Mirrored)> CandidateEccPairs()
    {
        EccLevel[] automatic = [EccLevel.Medium, EccLevel.Low];
        EccLevel[] normal = NormalEcc is { } n ? [n] : automatic;
        EccLevel[] mirrored = MirroredEcc is { } m ? [m] : automatic;

        return
        [
            .. from a in normal
               from b in mirrored
               orderby (int)a + (int)b descending
               select (a, b),
        ];
    }

    private static int ValidateVersion(int version) =>
        version is >= 1 and <= 40 ? version : throw new ArgumentOutOfRangeException(nameof(version), "QR versions range from 1 to 40.");
}