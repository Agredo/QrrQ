namespace QrrQ;

/// <summary>
/// Grund, warum kein Spiegel-QR-Code erzeugt werden konnte.
/// Geschlossene Hierarchie (C# 15): Alle Fälle stehen in dieser Datei,
/// ein <c>switch</c> darüber ist ohne Default-Zweig vollständig.
/// </summary>
public closed record class QrrQError;

/// <summary>Mindestens ein Text passt in keiner erlaubten Version.</summary>
/// <param name="NormalBits">Benötigte Bits für den normalen Text (in der größten Version).</param>
/// <param name="MirroredBits">Benötigte Bits für den gespiegelten Text.</param>
/// <param name="MaxVersion">Größte geprüfte Version.</param>
public sealed record class PayloadTooLong(int NormalBits, int MirroredBits, int MaxVersion) : QrrQError;

/// <summary>Die gewählten Fehlerkorrekturstufen lassen keinen Platz für zwei Inhalte.</summary>
/// <param name="Normal">Stufe für das normale Lesen.</param>
/// <param name="Mirrored">Stufe für das gespiegelte Lesen.</param>
public sealed record class IncompatibleEccLevels(EccLevel Normal, EccLevel Mirrored) : QrrQError;

/// <summary>Die Texte passen, aber im Versionsbereich wurde keine Lösung gefunden.</summary>
/// <param name="MinVersion">Kleinste geprüfte Version.</param>
/// <param name="MaxVersion">Größte geprüfte Version.</param>
/// <param name="MaxCorrectedCodewords">Erlaubte vorkorrigierte Codewörter.</param>
public sealed record class NoSolutionFound(int MinVersion, int MaxVersion, int MaxCorrectedCodewords) : QrrQError;

/// <summary>Erweiterungsmember für <see cref="QrrQError"/>.</summary>
public static class QrrQErrorExtensions
{
    extension(QrrQError error)
    {
        /// <summary>Lesbare Fehlerbeschreibung.</summary>
        public string Message => error switch
        {
            PayloadTooLong e =>
                $"Die Texte sind zu lang: normal {e.NormalBits} Bit, gespiegelt {e.MirroredBits} Bit – " +
                $"auch Version {e.MaxVersion} reicht nicht. Texte kürzen oder niedrigere Fehlerkorrektur wählen.",
            IncompatibleEccLevels e =>
                $"Mit {e.Normal.Letter}/{e.Mirrored.Letter} bleibt kein Platz für zwei Inhalte: Die Datenkapazitäten " +
                "beider Richtungen müssen zusammen größer sein als alle Codewörter. H nur mit L, Q mit L oder M kombinieren.",
            NoSolutionFound e =>
                $"Keine Lösung in Version {e.MinVersion}–{e.MaxVersion} " +
                (e.MaxCorrectedCodewords == 0
                    ? "ohne Fehlerkorrektur-Reserve. Tipp: MaxCorrectedCodewords > 0 erlauben oder den Versionsbereich erweitern."
                    : $"mit höchstens {e.MaxCorrectedCodewords} vorkorrigierten Codewörtern."),
        };
    }
}

/// <summary>Wird von <see cref="QrrQGenerator.Create"/> geworfen.</summary>
public sealed class QrrQException(QrrQError error) : Exception(error.Message)
{
    /// <summary>Der strukturierte Fehlergrund.</summary>
    public QrrQError Error { get; } = error;
}
