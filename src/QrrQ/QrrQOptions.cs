namespace QrrQ;

/// <summary>Einstellungen für <see cref="QrrQGenerator"/>.</summary>
public sealed record QrrQOptions
{
    /// <summary>Standardeinstellungen: automatische Fehlerkorrektur (M/L), Version 1–40, streng fehlerfrei.</summary>
    public static QrrQOptions Default { get; } = new();

    /// <summary>
    /// Fehlerkorrektur für das normale Lesen. <c>null</c> = automatisch (erst M, dann L).
    /// Hinweis: Die Datenkapazitäten beider Richtungen müssen zusammen größer sein als die
    /// Gesamtzahl der Codewörter – H geht daher nur mit L, Q mit L (oder knapp mit M).
    /// </summary>
    public EccLevel? NormalEcc { get; init; }

    /// <summary>Fehlerkorrektur für das gespiegelte Lesen. <c>null</c> = automatisch.</summary>
    public EccLevel? MirroredEcc { get; init; }

    /// <summary>Kleinste erlaubte QR-Version (1–40).</summary>
    public int MinVersion
    {
        get;
        init => field = ValidateVersion(value);
    } = 1;

    /// <summary>Größte erlaubte QR-Version (1–40).</summary>
    public int MaxVersion
    {
        get;
        init => field = ValidateVersion(value);
    } = 40;

    /// <summary>
    /// Wie viele Codewörter pro Leserichtung höchstens schon im gedruckten Code
    /// von der Fehlerkorrektur repariert werden dürfen. <c>0</c> (Standard) heißt:
    /// Beide Richtungen sind fehlerfreie Reed-Solomon-Codewörter. Werte &gt; 0 erlauben
    /// kleinere Codes für längere Texte, kosten aber Robustheit.
    /// </summary>
    public int MaxCorrectedCodewords
    {
        get;
        init => field = value >= 0 ? value : throw new ArgumentOutOfRangeException(nameof(MaxCorrectedCodewords));
    }

    /// <summary>Startwert für die frei wählbaren Bits – gleicher Seed, gleiche Eingabe, gleicher Code.</summary>
    public int Seed { get; init; } = 0x5152;

    /// <summary>Zufallsbelegungen pro Masken-Kombination; der optisch beste Kandidat gewinnt.</summary>
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
        version is >= 1 and <= 40 ? version : throw new ArgumentOutOfRangeException(nameof(version), "QR-Versionen reichen von 1 bis 40.");
}
