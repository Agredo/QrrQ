using QrrQ.Codec;
using QrrQ.Reading;
using QrrQ.Rendering;

namespace QrrQ;

/// <summary>
/// Ein fertiger Spiegel-QR-Code. <see cref="Matrix"/> ist das zu druckende Motiv;
/// normal gescannt ergibt es <see cref="NormalText"/>, im Spiegel <see cref="MirroredText"/>.
/// </summary>
public sealed class QrrQCode
{
    internal QrrQCode(MirrorSolution solution)
    {
        Matrix = solution.Matrix;
        NormalText = solution.Normal.Text;
        MirroredText = solution.Mirrored.Text;
        Version = solution.Normal.Version;
        NormalEcc = solution.Normal.Ecc;
        MirroredEcc = solution.Mirrored.Ecc;
        NormalMask = solution.Normal.Mask;
        MirroredMask = solution.Mirrored.Mask;
        NormalCorrectedCodewords = solution.Normal.CorrectedCodewords;
        MirroredCorrectedCodewords = solution.Mirrored.CorrectedCodewords;
        NormalFormatBitErrors = solution.Format.DistanceNormal;
        MirroredFormatBitErrors = solution.Format.DistanceMirrored;
    }

    /// <summary>Inhalt beim normalen Scannen.</summary>
    public string NormalText { get; }

    /// <summary>Inhalt beim Scannen des Spiegelbilds.</summary>
    public string MirroredText { get; }

    /// <summary>QR-Version 1–40.</summary>
    public int Version { get; }

    /// <summary>Kantenlänge in Modulen.</summary>
    public int Size => Matrix.Size;

    /// <summary>Fehlerkorrekturstufe der normalen Leserichtung.</summary>
    public EccLevel NormalEcc { get; }

    /// <summary>Fehlerkorrekturstufe der gespiegelten Leserichtung.</summary>
    public EccLevel MirroredEcc { get; }

    /// <summary>Datenmaske der normalen Leserichtung.</summary>
    public int NormalMask { get; }

    /// <summary>Datenmaske der gespiegelten Leserichtung.</summary>
    public int MirroredMask { get; }

    /// <summary>Codewörter, die beim normalen Lesen schon ab Werk korrigiert werden (0 = fehlerfrei).</summary>
    public int NormalCorrectedCodewords { get; }

    /// <summary>Codewörter, die beim gespiegelten Lesen schon ab Werk korrigiert werden (0 = fehlerfrei).</summary>
    public int MirroredCorrectedCodewords { get; }

    /// <summary>
    /// Bitabweichung der Formatinformation beim normalen Lesen (0 = exakt; bis 3 korrigiert jeder Scanner).
    /// Ein Formatwort ist gespiegelt nie gültig – je nach Maskenpaar wird daher 1 Bit „geliehen“.
    /// </summary>
    public int NormalFormatBitErrors { get; }

    /// <summary>Bitabweichung der Formatinformation beim gespiegelten Lesen.</summary>
    public int MirroredFormatBitErrors { get; }

    /// <summary>Beide Richtungen sind fehlerfreie Reed-Solomon-Codewörter.</summary>
    public bool IsErrorFree => NormalCorrectedCodewords == 0 && MirroredCorrectedCodewords == 0;

    /// <summary>Das zu druckende Modulraster.</summary>
    public QrMatrix Matrix { get; }

    /// <summary>Das Raster so, wie es im Spiegel erscheint (links/rechts vertauscht).</summary>
    public QrMatrix MirroredMatrix => field ??= Matrix.FlipHorizontal();

    /// <summary>PNG-Datei des Codes.</summary>
    /// <param name="moduleSize">Pixel pro Modul.</param>
    /// <param name="quietZone">Ruhezone in Modulen (Standard laut Norm: 4).</param>
    /// <param name="mirrored"><c>true</c> liefert das Spiegelbild – praktisch zum Testen am Bildschirm.</param>
    public byte[] ToPng(int moduleSize = 10, int quietZone = 4, bool mirrored = false)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(moduleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);
        return PngEncoder.Encode(mirrored ? MirroredMatrix : Matrix, moduleSize, quietZone);
    }

    /// <summary>Beide Ansichten als PNG: das Druckmotiv und sein Spiegelbild.</summary>
    public (byte[] Normal, byte[] Mirrored) ToPngPair(int moduleSize = 10, int quietZone = 4) =>
        (ToPng(moduleSize, quietZone), ToPng(moduleSize, quietZone, mirrored: true));

    /// <summary>SVG-Grafik des Codes.</summary>
    public string ToSvg(int moduleSize = 10, int quietZone = 4, bool mirrored = false, string darkColor = "#000", string lightColor = "#fff")
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(moduleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(quietZone);
        return SvgRenderer.Render(mirrored ? MirroredMatrix : Matrix, moduleSize, quietZone, darkColor, lightColor);
    }

    /// <summary>Konsolenvorschau aus Unicode-Halbblöcken.</summary>
    public string ToText(bool mirrored = false, bool darkBackground = true, int quietZone = 2) =>
        TextRenderer.Render(mirrored ? MirroredMatrix : Matrix, quietZone, darkBackground);

    /// <summary>Liest beide Ansichten mit dem eingebauten Decoder – wie ein Scanner nach der Bildverarbeitung.</summary>
    public (QrReadResult Normal, QrReadResult Mirrored) Verify() =>
        (QrReader.Read(Matrix), QrReader.Read(MirroredMatrix));

    /// <inheritdoc />
    public override string ToString() =>
        $"QrrQ V{Version} ({Size}×{Size}), {NormalEcc.Letter}/{MirroredEcc.Letter}: \"{NormalText}\" ⇄ \"{MirroredText}\"";
}
